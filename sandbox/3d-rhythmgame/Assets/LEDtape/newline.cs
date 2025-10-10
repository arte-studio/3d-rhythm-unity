using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using static UnityEngine.GraphicsBuffer;

public class newline : MonoBehaviour
{
    RenderTexture rd;
    Material[] mats;
    Material mtr;
    bool ready = false;
    [SerializeField] Shader shader;
    [SerializeField] int ID;
    [SerializeField] lineterm term;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rd = new RenderTexture(1, 120, 0);
        mtr = new Material(shader);
        mtr.SetTexture("_MainTex", rd);
        this.gameObject.GetComponentInChildren<Camera>().targetTexture = rd;
        mats = this.gameObject.GetComponent<MeshRenderer>().materials;
        mats[1] = mtr;
        this.gameObject.GetComponent<MeshRenderer>().materials = mats;
        ready = true;
    }
    private void Update()
    {
        if (term.ready && ready)
        {
            AsyncGPUReadback.Request(rd, 0, request => {
                if (request.hasError)
                {
                    Debug.LogError("Error.");
                }
                else
                {
                    var data = request.GetData<Color32>();

                    Color32[] colors = data.ToArray();
                    byte[] bytes = new byte[colors.Length * 3];
                    for (int i = 0; i < colors.Length; i++)
                    {
                        bytes[i * 3] = colors[i].r;
                        bytes[i * 3 + 1] = colors[i].g;
                        bytes[i * 3 + 2] = colors[i].b;
                    }
                    term.bytes[ID] = bytes;
                    if (ID == 0) Debug.Log("id " + ID + "'s first is " + colors[0].r);
                }
            });
        }
    }
}
