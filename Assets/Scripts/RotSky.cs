using UnityEngine;

public class RotSky : MonoBehaviour
{
    public float rotationSpeed = 1.0f; // Speed of rotation
    
    private Transform skyTransform;
    

    void Start()
    {
        skyTransform = transform; // Get the transform of the GameObject this script is attached to
    }
    // Update is called once per frame
    void Update()
    {
        // Rotate the sky around the Y-axis
        skyTransform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
    }
}
