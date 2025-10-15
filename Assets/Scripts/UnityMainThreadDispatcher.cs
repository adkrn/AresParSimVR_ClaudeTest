// UnityMainThreadDispatcher.cs
using System;
using System.Collections.Generic;
using UnityEngine;

public class UnityMainThreadDispatcher : MonoBehaviour
{
    static readonly Queue<Action> _jobs = new Queue<Action>();
    static UnityMainThreadDispatcher _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        if (_instance == null)
        {
            var go = new GameObject("UnityMainThreadDispatcher");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<UnityMainThreadDispatcher>();
        }
    }

    public static void Enqueue(Action job)
    {
        lock (_jobs)
        {
            _jobs.Enqueue(job);
        }
    }

    void Update()
    {
        // 매 프레임마다 큐에 담긴 job을 순차적으로 실행
        lock (_jobs)
        {
            while (_jobs.Count > 0)
            {
                _jobs.Dequeue()?.Invoke();
            }
        }
    }
}