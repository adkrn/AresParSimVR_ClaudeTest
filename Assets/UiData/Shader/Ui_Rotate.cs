using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RotateExample : MonoBehaviour
{
    void Update()
    {
        // ���� ������Ʈ �������� ȸ��
        transform.Rotate(new Vector3(0f, 0f, -120f) * Time.deltaTime);
            
    }
}