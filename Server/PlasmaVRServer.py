#!/usr/bin/env python3
"""
PlasmaVR Data Server
====================
HTTP server that streams simulation datasets to the PlasmaVR Quest app.
See SERVER_README.md for setup and usage instructions.
"""

from flask import Flask, jsonify, send_file, request
from flask_cors import CORS
import os
import sys
import socket
import argparse
import threading
import struct
import json
import random
import subprocess
import time
import uuid
from pathlib import Path
from werkzeug.utils import secure_filename

app = Flask(__name__)
CORS(app)  # Allow Quest to connect from different origin

# Data folder containing datasets
DATA_FOLDER = "./simulation_data"
SCREENSHOTS_FOLDER = "./screenshots"  # Default screenshots folder (can be overridden by GUI)
CONVERSION_CACHE = "./binary_cache"  # Cache for OBJ->Binary conversions
CONFIG_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "server_config.json")
DISCOVERY_PORT = 50000
PAIRING_TOKEN = None
SESSIONS = {}


def load_config() -> dict:
    """Load persisted settings from server_config.json."""
    try:
        with open(CONFIG_FILE, 'r') as f:
            return json.load(f)
    except Exception:
        return {}


def save_config(data: dict) -> None:
    """Persist settings to server_config.json."""
    try:
        existing = load_config()
        existing.update(data)
        with open(CONFIG_FILE, 'w') as f:
            json.dump(existing, f, indent=2)
    except Exception as e:
        print(f"[Config] Failed to save: {e}")


_cfg = load_config()
SERVER_NAME: str = _cfg.get("server_name") or socket.gethostname()  # Editable in GUI, persisted

def get_screenshots_folder():
    return SCREENSHOTS_FOLDER or "./screenshots"

def get_local_ip():
    """Get the local network IP address of this machine."""
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        # Connect to external address to determine local IP
        s.connect(('10.255.255.255', 1))
        ip = s.getsockname()[0]
    except Exception:
        ip = '127.0.0.1'
    finally:
        s.close()
    return ip

def get_folder_size(folder_path):
    """Calculate total size of a folder in bytes."""
    total_size = 0
    try:
        for dirpath, dirnames, filenames in os.walk(folder_path):
            for filename in filenames:
                filepath = os.path.join(dirpath, filename)
                if os.path.exists(filepath):
                    total_size += os.path.getsize(filepath)
    except Exception as e:
        print(f"Error calculating size: {e}")
    return total_size

def format_size(size_bytes):
    """Format bytes to human-readable string."""
    for unit in ['B', 'KB', 'MB', 'GB', 'TB']:
        if size_bytes < 1024.0:
            return f"{size_bytes:.1f} {unit}"
        size_bytes /= 1024.0
    return f"{size_bytes:.1f} PB"

def obj_to_binary(obj_file_path):
    """
    Convert OBJ mesh file to fast binary format.
    Binary format: [vertexCount:int32][triangleCount:int32][vertices:float32x3...][triangles:int32x3...]
    Expected 25x parsing speedup (1300ms OBJ -> 50ms binary on Quest).
    
    Returns: (success: bool, binary_data: bytes, error_message: str)
    """
    try:
        vertices = []
        triangles = []

        with open(obj_file_path, 'r') as f:
            for line in f:
                line = line.strip()
                
                # Skip empty lines and comments
                if not line or line.startswith('#'):
                    continue
                
                # Parse vertex
                if line.startswith('v '):
                    parts = line[2:].split()
                    if len(parts) >= 3:
                        try:
                            x, y, z = float(parts[0]), float(parts[1]), float(parts[2])
                            vertices.append((x, y, z))
                        except ValueError:
                            pass
                
                # Parse face
                elif line.startswith('f '):
                    parts = line[2:].split()
                    if len(parts) >= 3:
                        face_indices = []
                        for part in parts:
                            # Handle "v/vt/vn" format - extract vertex index only
                            vertex_ref = part.split('/')[0].strip()
                            try:
                                idx = int(vertex_ref) - 1  # OBJ indices are 1-based
                                if 0 <= idx < len(vertices):
                                    face_indices.append(idx)
                            except ValueError:
                                pass
                        
                        # Triangulate (support triangles and quads and polygons)
                        if len(face_indices) >= 3:
                            # Fan triangulation
                            for i in range(1, len(face_indices) - 1):
                                triangles.append(face_indices[0])
                                triangles.append(face_indices[i])
                                triangles.append(face_indices[i + 1])
        
        print(f"[OBJ->BIN-CONVERT] Parsed {len(vertices)} vertices, {len(triangles)//3} triangles", flush=True)
        
        # Write binary format (even if no triangles - allow point clouds)
        vertex_count = len(vertices)
        triangle_count = len(triangles) // 3
        
        binary_data = bytearray()
        
        # Header
        binary_data.extend(struct.pack('<I', vertex_count))      # vertexCount
        binary_data.extend(struct.pack('<I', triangle_count))    # triangleCount
        
        # Vertices
        for x, y, z in vertices:
            binary_data.extend(struct.pack('<fff', x, y, z))
        
        # Triangles
        for idx in triangles:
            binary_data.extend(struct.pack('<I', idx))
        
        if vertex_count == 0:
            return (False, None, "No vertices found in OBJ file")
        
        return (True, bytes(binary_data), None)
        
    except Exception as e:
        return (False, None, f"Parse failed: {str(e)}")

def get_or_convert_isosurface(dataset_name, file_path):
    """
    Get an isosurface file, converting OBJ to binary if needed.
    
    Returns: (file_path_to_serve: str, is_binary: bool)
    """
    full_path = os.path.join(DATA_FOLDER, dataset_name, file_path)
    
    # If it's not an OBJ file, serve as-is
    if not file_path.endswith('.obj'):
        print(f"[OBJ->BIN] Not OBJ format: {file_path}", flush=True)
        return (full_path, False)
    
    # For OBJ files, check if binary cache exists
    os.makedirs(CONVERSION_CACHE, exist_ok=True)
    
    # Create cache path: cache/<dataset_name>/<frame>.bin
    cache_subdir = os.path.join(CONVERSION_CACHE, dataset_name)
    os.makedirs(cache_subdir, exist_ok=True)
    
    base_name = os.path.splitext(os.path.basename(file_path))[0]
    binary_cache_path = os.path.join(cache_subdir, f"{base_name}.bin")
    
    # Check if binary cache exists
    if os.path.exists(binary_cache_path):
        print(f"[OBJ->BIN] Using cached binary: {base_name}.bin", flush=True)
        return (binary_cache_path, True)
    
    # Check if original OBJ exists
    if not os.path.exists(full_path):
        print(f"[OBJ->BIN] OBJ file does not exist: {full_path}", flush=True)
        return (full_path, False)  # Will 404 downstream
    
    # Convert OBJ to binary
    print(f"[OBJ->BIN] Converting {file_path}...", end='', flush=True)
    success, binary_data, error = obj_to_binary(full_path)
    
    if not success:
        print(f" ERROR: {error}", flush=True)
        return (full_path, False)  # Fall back to OBJ (slower but works)
    
    # Cache the binary
    try:
        with open(binary_cache_path, 'wb') as f:
            f.write(binary_data)
        
        obj_size = os.path.getsize(full_path)
        bin_size = len(binary_data)
        ratio = (1 - bin_size / obj_size) * 100 if obj_size > 0 else 0
        
        print(f" done! ({format_size(obj_size)} OBJ -> {format_size(bin_size)} BIN, {ratio:.0f}% smaller)", flush=True)
        return (binary_cache_path, True)
    
    except Exception as e:
        print(f" CACHE FAILED: {e}", flush=True)
        return (full_path, False)

def consolidate_streamline_frame(frame_dir):
    """
    Consolidate 200 separate .raw streamline files into single binary file.
    
    Format: [int32 numLines]
            [for each line: int32 vertexCount, then float32x4 (x,y,z,mag) for each vertex]
    
    Expected speedup: 200 file reads → 1 file read (massive I/O reduction on Quest).
    
    Returns: (success: bool, binary_data: bytes, error_message: str)
    """
    try:
        if not os.path.isdir(frame_dir):
            return (False, None, "Frame directory not found")
        
        lines_data = []
        num_lines = 200
        
        for line_idx in range(num_lines):
            line_file = os.path.join(frame_dir, f"{line_idx}.raw")
            if not os.path.exists(line_file):
                break
            
            # Read raw streamline file (format: float32 x, y, z, magnitude per vertex)
            with open(line_file, 'rb') as f:
                line_bytes = f.read()
            
            if not line_bytes:
                break
            
            # Each vertex is 16 bytes (4 floats)
            vertex_count = len(line_bytes) // 16
            if vertex_count == 0:
                break
            
            lines_data.append((vertex_count, line_bytes))
        
        if not lines_data:
            return (False, None, "No streamline data found")
        
        # Build consolidated binary
        binary_data = bytearray()
        
        # Header: number of lines
        binary_data.extend(struct.pack('<I', len(lines_data)))
        
        # For each line: vertex count + raw vertex data
        for vertex_count, line_bytes in lines_data:
            binary_data.extend(struct.pack('<I', vertex_count))
            binary_data.extend(line_bytes)
        
        total_vertices = sum(vc for vc, _ in lines_data)
        print(f"[STREAMLINE-CONSOLIDATE] {len(lines_data)} lines, {total_vertices} vertices", flush=True)
        
        return (True, bytes(binary_data), None)
        
    except Exception as e:
        return (False, None, f"Consolidation failed: {str(e)}")

def get_or_convert_streamline_frame(dataset_name, frame_number):
    """
    Get a streamline frame, consolidating 200 .raw files into one .sbin if needed.
    
    Returns: (file_path_to_serve: str, is_consolidated: bool)
    """
    # Frame directory path (Vectors/0, Vectors/1, etc.)
    frame_dir = os.path.join(DATA_FOLDER, dataset_name, "Vectors", str(frame_number))
    
    # Check if directory exists (try Streamlines as fallback)
    if not os.path.isdir(frame_dir):
        frame_dir = os.path.join(DATA_FOLDER, dataset_name, "Streamlines", str(frame_number))
    
    if not os.path.isdir(frame_dir):
        print(f"[STREAMLINE-CONVERT] Frame directory not found: {frame_number}", flush=True)
        return (frame_dir, False)  # Will 404 downstream
    
    # Check cache for consolidated file
    os.makedirs(CONVERSION_CACHE, exist_ok=True)
    cache_subdir = os.path.join(CONVERSION_CACHE, dataset_name, "streamlines")
    os.makedirs(cache_subdir, exist_ok=True)
    
    consolidated_cache_path = os.path.join(cache_subdir, f"{frame_number}.sbin")
    
    # Return cached if exists
    if os.path.exists(consolidated_cache_path):
        print(f"[STREAMLINE-CONVERT] Using cached: frame {frame_number}.sbin", flush=True)
        return (consolidated_cache_path, True)
    
    # Consolidate 200 files into one
    print(f"[STREAMLINE-CONVERT] Consolidating frame {frame_number}...", end='', flush=True)
    success, binary_data, error = consolidate_streamline_frame(frame_dir)
    
    if not success:
        print(f" ERROR: {error}", flush=True)
        return (frame_dir, False)  # Fall back to individual files (slower)
    
    # Cache the consolidated binary
    try:
        with open(consolidated_cache_path, 'wb') as f:
            f.write(binary_data)
        
        # Calculate original size (sum of 200 .raw files)
        original_size = 0
        for i in range(200):
            raw_file = os.path.join(frame_dir, f"{i}.raw")
            if os.path.exists(raw_file):
                original_size += os.path.getsize(raw_file)
            else:
                break
        
        consolidated_size = len(binary_data)
        overhead = ((consolidated_size / original_size) - 1) * 100 if original_size > 0 else 0
        
        print(f" done! ({format_size(original_size)} -> {format_size(consolidated_size)}, +{overhead:.1f}% header overhead, but 200x fewer file reads!)", flush=True)
        return (consolidated_cache_path, True)
    
    except Exception as e:
        print(f" CACHE FAILED: {e}", flush=True)
        return (frame_dir, False)

def scan_datasets():
    """
    Scan the data folder for datasets.
    Each dataset is a folder containing info.txt and subfolders for data types.
    """
    datasets = []
    
    if not os.path.exists(DATA_FOLDER):
        print(f"WARNING: Data folder not found: {DATA_FOLDER}")
        print(f"Creating empty data folder...")
        os.makedirs(DATA_FOLDER, exist_ok=True)
        return datasets
    
    for folder_name in os.listdir(DATA_FOLDER):
        dataset_path = os.path.join(DATA_FOLDER, folder_name)
        
        # Skip if not a directory
        if not os.path.isdir(dataset_path):
            continue
        
        # Check for info.txt
        info_path = os.path.join(dataset_path, "info.txt")
        if not os.path.exists(info_path):
            print(f"WARNING: Skipping {folder_name} - no info.txt found")
            continue
        
        # Parse info.txt to get frame count
        try:
            with open(info_path, 'r') as f:
                first_line = f.readline().strip()
                max_frames = int(first_line.split(';')[0])
        except Exception as e:
            print(f"WARNING: Error parsing info.txt in {folder_name}: {e}")
            max_frames = 0
        
        # Calculate dataset size
        folder_size = get_folder_size(dataset_path)
        
        datasets.append({
            "name": folder_name,
            "frames": max_frames,
            "size": format_size(folder_size),
            "size_bytes": folder_size
        })
    
    return datasets

@app.route('/health', methods=['GET'])
def health_check():
    """Health check endpoint - Quest uses this to test connection."""
    return jsonify({"status": "ok", "server": "PlasmaVR Data Server"})

@app.route('/api/datasets', methods=['GET'])
def list_datasets():
    """
    Get list of available datasets.
    
    Response format:
    {
        "datasets": [
            {"name": "Dataset1", "frames": 500, "size": "1.2 GB"},
            {"name": "Dataset2", "frames": 300, "size": "800 MB"}
        ]
    }
    """
    try:
        datasets = scan_datasets()
        return jsonify({"datasets": datasets})
    except Exception as e:
        return jsonify({"error": str(e)}), 500

@app.route('/api/dataset/<dataset_name>/info', methods=['GET'])
def get_dataset_info(dataset_name):
    """
    Get info.txt content for a specific dataset.
    
    Response format:
    {
        "info": "500;WindTunnel;Simulation of wind tunnel\\nX;256;0;1;m\\n..."
    }
    """
    try:
        info_path = os.path.join(DATA_FOLDER, dataset_name, "info.txt")
        
        if not os.path.exists(info_path):
            return jsonify({"error": f"info.txt not found for dataset: {dataset_name}"}), 404
        
        with open(info_path, 'r') as f:
            content = f.read()
        
        return jsonify({"info": content})
    
    except Exception as e:
        return jsonify({"error": str(e)}), 500

@app.route('/api/dataset/<dataset_name>/download', methods=['GET'])
def download_dataset(dataset_name):
    """
    Get full file list for dataset (for bulk download).
    Returns all files that need to be downloaded.
    
    Response format:
    {
        "dataset": "DatasetName",
        "total_files": 1500,
        "total_size": 1073741824,
        "total_size_formatted": "1.0 GB",
        "files": [
            {"path": "Particles/0.raw", "size": 1024},
            {"path": "Isosurfaces/0.obj", "size": 2048}
        ]
    }
    """
    try:
        dataset_path = os.path.join(DATA_FOLDER, dataset_name)
        
        if not os.path.exists(dataset_path):
            return jsonify({"error": f"Dataset not found: {dataset_name}"}), 404
        
        files = []
        total_size = 0
        for root, dirs, filenames in os.walk(dataset_path):
            for filename in filenames:
                filepath = os.path.join(root, filename)
                relpath = os.path.relpath(filepath, dataset_path)
                
                # Skip info.txt (already fetched separately)
                if relpath == "info.txt":
                    continue
                
                # Get file size
                file_size = os.path.getsize(filepath)
                total_size += file_size
                
                # Normalize path separators for web
                relpath = relpath.replace(os.sep, '/')
                
                files.append({
                    "path": relpath,
                    "size": file_size
                })
        
        return jsonify({
            "dataset": dataset_name,
            "total_files": len(files),
            "total_size": total_size,
            "total_size_formatted": format_size(total_size),
            "files": files
        })
    
    except Exception as e:
        return jsonify({"error": str(e)}), 500

@app.route('/api/dataset/<dataset_name>/progress', methods=['GET'])
def get_dataset_progress(dataset_name):
    """
    Check if a dataset is complete on the server.
    Useful for validating before downloading.
    
    Response format:
    {
        "dataset": "DatasetName",
        "expected_frames": 500,
        "particle_frames": 450,
        "isosurface_frames": 500,
        "streamline_frames": 120,
        "complete": false,
        "missing_folders": ["Streamlines"]
    }
    """
    try:
        dataset_path = os.path.join(DATA_FOLDER, dataset_name)
        
        if not os.path.exists(dataset_path):
            return jsonify({"error": f"Dataset not found: {dataset_name}"}), 404
        
        # Get expected frame count from info.txt
        info_path = os.path.join(dataset_path, "info.txt")
        expected_frames = 0
        if os.path.exists(info_path):
            try:
                with open(info_path, 'r') as f:
                    first_line = f.readline().strip()
                    expected_frames = int(first_line.split(';')[0])
            except:
                pass
        
        # Count frames in each category
        def count_frames_in_folder(folder_name):
            folder_path = os.path.join(dataset_path, folder_name)
            if not os.path.exists(folder_path):
                return 0
            try:
                return len([f for f in os.listdir(folder_path) if f.endswith('.raw')])
            except:
                return 0
        
        particle_frames = count_frames_in_folder("Particles")
        isosurface_frames = count_frames_in_folder("Isosurfaces")
        streamline_frames = count_frames_in_folder("Streamlines")
        
        # Check for missing folders
        missing_folders = []
        if particle_frames == 0:
            missing_folders.append("Particles")
        if isosurface_frames == 0:
            missing_folders.append("Isosurfaces")
        if streamline_frames == 0:
            missing_folders.append("Streamlines")
        
        # Dataset is complete if all data types have expected frames
        is_complete = (particle_frames == expected_frames and 
                      isosurface_frames == expected_frames and 
                      streamline_frames == expected_frames and
                      expected_frames > 0)
        
        return jsonify({
            "dataset": dataset_name,
            "expected_frames": expected_frames,
            "particle_frames": particle_frames,
            "isosurface_frames": isosurface_frames,
            "streamline_frames": streamline_frames,
            "complete": is_complete,
            "missing_folders": missing_folders
        })
    
    except Exception as e:
        return jsonify({"error": str(e)}), 500

@app.route('/api/screenshot/upload', methods=['POST'])
def upload_screenshot():
    """Upload a screenshot from the headset.

    Expects multipart form-data with field name 'file'.
    """
    os.makedirs(SCREENSHOTS_FOLDER, exist_ok=True)

    try:
        if 'file' not in request.files:
            return jsonify({"error": "No file part"}), 400

        file = request.files['file']
        if file.filename == '':
            return jsonify({"error": "Empty filename"}), 400

        filename = secure_filename(file.filename)
        if not filename:
            filename = "screenshot.png"

        screenshots_dir = get_screenshots_folder()
        os.makedirs(screenshots_dir, exist_ok=True)
        save_path = os.path.join(screenshots_dir, filename)

        total_bytes = file.content_length or request.content_length
        if total_bytes and total_bytes > 0:
            bytes_read = 0
            with open(save_path, "wb") as f:
                while True:
                    chunk = file.stream.read(1024 * 1024)
                    if not chunk:
                        break
                    f.write(chunk)
                    bytes_read += len(chunk)
                    percent = (bytes_read / total_bytes) * 100
                    sys.stdout.write(
                        f"\r[Upload] {filename}: {percent:6.2f}% "
                        f"({format_size(bytes_read)} / {format_size(total_bytes)})"
                    )
                    sys.stdout.flush()
            sys.stdout.write("\n")
        else:
            file.save(save_path)

        return jsonify({
            "status": "ok",
            "filename": filename,
            "path": save_path
        })
    except Exception as e:
        return jsonify({"error": str(e)}), 500

@app.route('/dataset/<dataset_name>/<path:file_path>')
def stream_file(dataset_name, file_path):
    """
    Stream any file from a dataset.
    Automatically converts OBJ isosurfaces to binary format for Quest performance.
    Automatically consolidates streamline frames (200 files → 1 file) for Quest I/O performance.
    
    Examples:
    /dataset/WindTunnel/Particles/0.raw
    /dataset/WindTunnel/Isosurfaces/5.obj (auto-converted to 5.bin)
    /dataset/WindTunnel/Vectors/10 (auto-consolidated to 10.sbin)
    /dataset/WindTunnel/info.txt
    """
    try:
        # For streamline frame directory requests, consolidate 200 files → 1
        # Client requests: /dataset/DatasetName/Vectors/0, /Vectors/1, etc.
        if ('Vectors/' in file_path or 'Streamlines/' in file_path):
            # Extract frame number from path (e.g., "Vectors/0" -> 0)
            parts = file_path.split('/')
            if len(parts) >= 2:
                try:
                    frame_number = int(parts[-1])
                    print(f"[ROUTE] Streamline frame request: {dataset_name}/{file_path} (frame {frame_number})", flush=True)
                    full_path, is_consolidated = get_or_convert_streamline_frame(dataset_name, frame_number)
                    
                    # If consolidated, serve the .sbin file directly
                    if is_consolidated and os.path.isfile(full_path):
                        print(f"[ROUTE] Serving consolidated streamline: {full_path}", flush=True)
                        file_size = os.path.getsize(full_path)
                        print(f"[Download] {dataset_name}/{file_path} ({format_size(file_size)}) - sending...", end='', flush=True)
                        response = send_file(full_path, mimetype='application/octet-stream')
                        print(f" done!", flush=True)
                        return response
                    
                except (ValueError, IndexError):
                    pass  # Not a frame number, continue to regular file handling
        
        # For isosurface paths, check if conversion is needed
        if 'Isosurfaces' in file_path or 'Scalars' in file_path:
            print(f"[ROUTE] Isosurface request: {dataset_name}/{file_path}", flush=True)
            full_path, is_binary = get_or_convert_isosurface(dataset_name, file_path)
            print(f"[ROUTE] Serving from: {full_path} (binary={is_binary})", flush=True)
        else:
            print(f"[ROUTE] Regular file request: {dataset_name}/{file_path}", flush=True)
            full_path = os.path.join(DATA_FOLDER, dataset_name, file_path)
        
        # Security: prevent path traversal
        real_path = os.path.realpath(full_path)
        real_data_folder = os.path.realpath(DATA_FOLDER)
        cache_folder = os.path.realpath(CONVERSION_CACHE)
        
        if not (real_path.startswith(real_data_folder) or real_path.startswith(cache_folder)):
            return jsonify({"error": "Invalid file path"}), 403
        
        if not os.path.exists(full_path):
            return jsonify({"error": f"File not found: {full_path}"}), 404
        
        # Get file size and log transfer start
        file_size = os.path.getsize(full_path)
        print(f"[Download] {dataset_name}/{file_path} ({format_size(file_size)}) - sending...", end='', flush=True)
        
        # Stream the file
        response = send_file(full_path, mimetype='application/octet-stream')
        
        # Log completion
        print(f" done!", flush=True)
        
        return response
    
    except Exception as e:
        print(f" ERROR: {e}", flush=True)
        return jsonify({"error": str(e)}), 500

def print_banner(port):
    """Print startup banner with connection information."""
    local_ip = get_local_ip()
    datasets = scan_datasets()
    
    print("=" * 60)
    print("  PlasmaVR Data Server Started!")
    print("=" * 60)
    print(f"  Local IP:       {local_ip}")
    print(f"  Port:           {port}")
    print(f"  Quest URL:      http://{local_ip}:{port}")
    print(f"  Data Folder:    {os.path.abspath(DATA_FOLDER)}")
    print(f"  Datasets:       {len(datasets)}")
    
    if datasets:
        print("\n  Available Datasets:")
        for ds in datasets:
            print(f"    • {ds['name']} ({ds['frames']} frames, {ds['size']})")
    else:
        print("\n  ⚠ WARNING: No datasets found!")
        print(f"  Create datasets in: {os.path.abspath(DATA_FOLDER)}")
    
    print("\n  Press Ctrl+C to stop server")
    print("=" * 60)
    print()


def start_udp_discovery(service_name, server_url, token, port=DISCOVERY_PORT):
    """Start a simple UDP discovery responder.

    Clients broadcast the literal message 'PLASMAVR_DISCOVER' to port DISCOVERY_PORT.
    The server replies to the sender with a JSON payload containing name/url/token.
    """
    def responder():
        try:
            sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            sock.bind(("", port))
            print(f"[DISCOVERY] UDP discovery responder listening on port {port}", flush=True)

            # Use SERVER_NAME global so GUI changes are reflected immediately.
            while True:
                try:
                    data, addr = sock.recvfrom(4096)
                    if not data:
                        continue
                    msg = data.decode('utf-8', errors='ignore').strip()
                    if msg == 'PLASMAVR_DISCOVER':
                        payload = json.dumps({
                            'name': SERVER_NAME,
                            'url': server_url,
                            'token': token
                        })
                        sock.sendto(payload.encode('utf-8'), addr)
                        print(f"[DISCOVERY] Responded to discovery from {addr}", flush=True)
                except Exception as e:
                    # continue listening
                    print(f"[DISCOVERY] Error in responder loop: {e}", flush=True)
        except Exception as e:
            print(f"[DISCOVERY] Failed to start discovery responder: {e}", flush=True)

    t = threading.Thread(target=responder, daemon=True)
    t.start()


@app.route('/pair', methods=['POST'])
def pair_endpoint():
    """Pairing endpoint: client sends {'token': '123456'} and receives a session token.

    This endpoint is intentionally simple: it validates the provided token matches
    the current PAIRING_TOKEN and returns a session token (UUID) with short expiry.
    """
    try:
        data = request.get_json(force=True)
        if not data or 'token' not in data:
            return jsonify({'error': 'missing token'}), 400

        token = str(data['token'])
        if PAIRING_TOKEN is None:
            return jsonify({'error': 'pairing not available'}), 403

        if token != PAIRING_TOKEN:
            return jsonify({'error': 'invalid token'}), 403

        # Issue a session token
        session = str(uuid.uuid4())
        expires = int(time.time()) + 3600  # 1 hour
        SESSIONS[session] = expires

        return jsonify({'session_token': session, 'expires': expires})
    except Exception as e:
        return jsonify({'error': str(e)}), 500

def build_server_url(host, port):
    """Build the server URL for clients (use local IP when host is 0.0.0.0)."""
    if host in ("0.0.0.0", "127.0.0.1", "localhost"):
        host = get_local_ip()
    return f"http://{host}:{port}"

def run_server(host, port):
    """Run the Flask server."""
    app.run(host=host, port=port, debug=False, threaded=True)

def run_server_wsgi(host, port):
    """Run the server with a WSGI server (waitress)."""
    try:
        from waitress import serve
    except Exception as e:
        print(f"[WSGI] Waitress not installed: {e}")
        print("[WSGI] Install with: pip install waitress")
        return
    serve(app, host=host, port=port)

def run_gui(host, port):
    """Run a simple GUI that shows the server URL and controls."""
    try:
        import tkinter as tk
        from tkinter import ttk, filedialog
    except Exception as e:
        print(f"[GUI] Missing GUI dependencies: {e}")
        return

    def open_folder(path):
        """Open a folder in the native file manager (Windows/macOS/Linux)."""
        path = os.path.abspath(path)
        os.makedirs(path, exist_ok=True)
        if sys.platform == "win32":
            os.startfile(path)
        elif sys.platform == "darwin":
            subprocess.Popen(["open", path])
        else:
            subprocess.Popen(["xdg-open", path])

    url = build_server_url(host, port)

    root = tk.Tk()
    root.title("PlasmaVR Server")
    root.geometry("780x420")
    root.resizable(True, False)

    # ── Title ────────────────────────────────────────────────────────────────
    ttk.Label(root, text="PlasmaVR Server", font=("Segoe UI", 13, "bold")).pack(pady=(16, 2))
    ttk.Label(root, text="Server is running and ready for connections.",
              font=("Segoe UI", 9)).pack(pady=(0, 10))

    ttk.Separator(root, orient="horizontal").pack(fill="x", padx=24, pady=(0, 10))

    # ── URL block ────────────────────────────────────────────────────────────
    bg = root.cget('bg')
    try:
        r, g, b = root.winfo_rgb(bg)
        luminance = 0.2126*(r/65535) + 0.7152*(g/65535) + 0.0722*(b/65535)
        fg = '#1a1a1a' if luminance > 0.5 else '#ffffff'
    except Exception:
        fg = '#1a1a1a'

    tk.Label(root, text=url, font=("Segoe UI", 26, "bold"),
             fg=fg, bg=bg, justify='center').pack(pady=(0, 6))

    def copy_url():
        root.clipboard_clear()
        root.clipboard_append(url)
        root.update()

    ttk.Button(root, text="Copy URL", command=copy_url).pack(pady=(0, 4))
    ttk.Label(root, text="Type or copy this URL into your headset to connect.",
              font=("Segoe UI", 9)).pack(pady=(0, 10))

    ttk.Separator(root, orient="horizontal").pack(fill="x", padx=24, pady=(0, 10))

    # ── Settings grid (server name / data folder / screenshots) ─────────────
    grid_frame = ttk.Frame(root)
    grid_frame.pack(fill="x", padx=24, pady=(0, 12))

    # Column weights so the entry/path column stretches
    grid_frame.columnconfigure(1, weight=1)

    # Row 0 – Server name
    ttk.Label(grid_frame, text="Server Name:", anchor="e").grid(
        row=0, column=0, sticky="e", padx=(0, 8), pady=4)

    name_var = tk.StringVar(value=SERVER_NAME)

    def _limit_name(*args):
        v = name_var.get()
        if len(v) > 17:
            name_var.set(v[:17])
    name_var.trace_add("write", _limit_name)

    name_entry = ttk.Entry(grid_frame, textvariable=name_var, width=32)
    name_entry.grid(row=0, column=1, sticky="ew", pady=4)

    def apply_server_name():
        global SERVER_NAME
        new_name = name_var.get().strip()
        if new_name:
            SERVER_NAME = new_name
            save_config({"server_name": SERVER_NAME})

    ttk.Button(grid_frame, text="Apply", command=apply_server_name, width=10).grid(
        row=0, column=2, padx=(8, 0), pady=4)

    # Row 1 – Data folder
    ttk.Label(grid_frame, text="Data Folder:", anchor="e").grid(
        row=1, column=0, sticky="e", padx=(0, 8), pady=4)

    folder_var = tk.StringVar(value=os.path.abspath(DATA_FOLDER))
    folder_entry = ttk.Entry(grid_frame, textvariable=folder_var, state="readonly")
    folder_entry.grid(row=1, column=1, sticky="ew", pady=4)

    def choose_folder():
        global DATA_FOLDER
        selected = filedialog.askdirectory(title="Select Dataset Folder",
                                           initialdir=os.path.abspath(DATA_FOLDER))
        if selected:
            DATA_FOLDER = selected
            os.makedirs(DATA_FOLDER, exist_ok=True)
            folder_var.set(os.path.abspath(DATA_FOLDER))

    ttk.Button(grid_frame, text="Browse…", command=choose_folder, width=10).grid(
        row=1, column=2, padx=(8, 0), pady=4)
    ttk.Button(grid_frame, text="Open", command=lambda: open_folder(DATA_FOLDER), width=8).grid(
        row=1, column=3, padx=(4, 0), pady=4)

    # Row 2 – Screenshots folder
    ttk.Label(grid_frame, text="Screenshots:", anchor="e").grid(
        row=2, column=0, sticky="e", padx=(0, 8), pady=4)

    shots_var = tk.StringVar(value=os.path.abspath(get_screenshots_folder()))
    shots_entry = ttk.Entry(grid_frame, textvariable=shots_var, state="readonly")
    shots_entry.grid(row=2, column=1, sticky="ew", pady=4)

    def choose_screenshots_folder():
        global SCREENSHOTS_FOLDER
        selected = filedialog.askdirectory(title="Select Screenshots Folder",
                                           initialdir=os.path.abspath(get_screenshots_folder()))
        if selected:
            SCREENSHOTS_FOLDER = selected
            os.makedirs(SCREENSHOTS_FOLDER, exist_ok=True)
            shots_var.set(os.path.abspath(SCREENSHOTS_FOLDER))

    ttk.Button(grid_frame, text="Browse…", command=choose_screenshots_folder, width=10).grid(
        row=2, column=2, padx=(8, 0), pady=4)
    ttk.Button(grid_frame, text="Open", command=lambda: open_folder(get_screenshots_folder()), width=8).grid(
        row=2, column=3, padx=(4, 0), pady=4)

    root.mainloop()


if __name__ == '__main__':
    # Parse command line arguments
    parser = argparse.ArgumentParser(description='PlasmaVR Data Server')
    parser.add_argument('--data-folder', type=str, default='./simulation_data',
                        help='Path to folder containing datasets')
    parser.add_argument('--port', type=int, default=8080,
                        help='Port to run server on')
    parser.add_argument('--host', type=str, default='0.0.0.0',
                        help='Host to bind to (0.0.0.0 = all interfaces)')
    parser.add_argument('--no-gui', action='store_true',
                        help='Disable GUI (run server in console only)')
    parser.add_argument('--no-wsgi', action='store_true',
                        help='Use Flask dev server instead of WSGI (waitress)')
    
    args = parser.parse_args()
    
    # Set data folder
    DATA_FOLDER = args.data_folder
    
    # Create data folder if it doesn't exist
    os.makedirs(DATA_FOLDER, exist_ok=True)
    
    # Print startup banner
    print_banner(args.port)
    
    # Run server (GUI optional)
    try:
        server_runner = run_server if args.no_wsgi else run_server_wsgi

        # Start discovery responder (UDP) so clients can find this server on LAN.
        server_url = build_server_url(args.host, args.port)
        service_name = os.path.basename(os.path.abspath(DATA_FOLDER)) or 'PlasmaVR Server'
        pairing_token = f"{random.randint(100000,999999)}"
        # Expose pairing token for pairing endpoint and GUI
        PAIRING_TOKEN = pairing_token
        # Don't print the actual pairing token to console for privacy
        print("[DISCOVERY] Pairing token generated (hidden)")
        start_udp_discovery(service_name, server_url, pairing_token)

        if not args.no_gui:
            server_thread = threading.Thread(target=server_runner, args=(args.host, args.port), daemon=True)
            server_thread.start()
            run_gui(args.host, args.port)
        else:
            server_runner(args.host, args.port)
    except KeyboardInterrupt:
        print("\n\nServer stopped by user.")
        sys.exit(0)
