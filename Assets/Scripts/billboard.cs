using UnityEngine;


public class Billboard : MonoBehaviour
{
    void OnEnable()
    {
        if (!GetComponent<Renderer>().isVisible)
        {
            enabled = false;
        }
    }


    void LateUpdate()
    {
        transform.forward = Camera.main.transform.forward;
    }

    void OnBecameVisible()
    {
        enabled = true;
    }

    void OnBecameInvisible()
    {
        enabled = false;
    }
}