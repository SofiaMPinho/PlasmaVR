using Dummiesman;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class ObjFromStream : MonoBehaviour {
	void Start () {
        StartCoroutine(LoadObjFromUrl());
	}
    
    private IEnumerator LoadObjFromUrl()
    {
        //make UnityWebRequest
        using (UnityWebRequest webRequest = UnityWebRequest.Get("https://people.sc.fsu.edu/~jburkardt/data/obj/lamp.obj"))
        {
            yield return webRequest.SendWebRequest();
            
            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                //create stream and load
                var textStream = new MemoryStream(Encoding.UTF8.GetBytes(webRequest.downloadHandler.text));
                var loadedObj = new OBJLoader().Load(textStream);
            }
            else
            {
                Debug.LogError($"Failed to download OBJ file: {webRequest.error}");
            }
        }
    }
}
