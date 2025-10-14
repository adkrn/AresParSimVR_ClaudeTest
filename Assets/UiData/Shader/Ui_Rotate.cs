using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RotateExample : MonoBehaviour
{
    void Update()
    {
        // 게임 오브젝트 기준으로 회전
        transform.Rotate(new Vector3(0f, 0f, -120f) * Time.deltaTime);
            
    }
}