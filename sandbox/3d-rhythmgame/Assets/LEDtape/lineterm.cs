using System;
using System.Collections;
using UnityEngine;

public class lineterm : MonoBehaviour
{
    public byte[][] bytes;
    public bool ready = false;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        bytes = new byte[90][];

        ready = true;

        //Invoke("get1to4", 1f);
    }

    void get1to4()
    {
        //テスト用関数
        GetBytes(0, 3);
    }

    // Update is called once per frame
    public byte[] GetBytes(int begin, int end)
    {
        ArrayList list = new ArrayList(bytes[begin]);
        for (int i = begin +1; i <= end; i++)
        {
            list.AddRange(bytes[i]);
        }
        byte[] bt = (byte[])list.ToArray(typeof(byte));
        Debug.Log("bt length " + bt.Length);
        return bt;
    }
    /*private void Update()
    {
        Debug.Log(bytes[0][0]);
    }*/
}
