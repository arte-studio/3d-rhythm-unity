using System;
using System.Collections.Generic;
using UnityEngine;

// マルチスレッドで受信したデータをUnityのメインスレッドで安全に処理するため
// 別スレッドから受け取った処理（この場合はDebug.Log）をキューに入れ、Unityのメインスレッドで実行されるUpdate()関数内で順番に処理することで、スレッド間の同期の問題を解決

public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static readonly Queue<Action> _executionQueue = new Queue<Action>();

    public void Update()
    {
        lock (_executionQueue)
        {
            while (_executionQueue.Count > 0)
            {
                _executionQueue.Dequeue().Invoke();
            }
        }
    }

    public static void Enqueue(Action action)
    {
        lock (_executionQueue)
        {
            _executionQueue.Enqueue(action);
        }
    }

    private static UnityMainThreadDispatcher _instance;

    public static UnityMainThreadDispatcher Instance()
    {
        if (_instance == null)
        {
            GameObject dispatcherObject = new GameObject("UnityMainThreadDispatcher");
            _instance = dispatcherObject.AddComponent<UnityMainThreadDispatcher>();
            DontDestroyOnLoad(dispatcherObject);
        }
        return _instance;
    }

    void OnApplicationQuit()
    {
        _instance = null;
    }
}