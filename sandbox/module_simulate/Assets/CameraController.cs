using UnityEngine;

public class CameraController : MonoBehaviour
{
    void Start()
    {
        // ’†S‚ğ(0, 1.5, 0)‚ÉŒ©‰º‚ë‚·‚æ‚¤”z’u
        transform.position = new Vector3(0, 1.5f, -3f);
        transform.LookAt(new Vector3(0, 1.5f, 0));
    }
}
