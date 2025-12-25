using UnityEngine;

public class Trigger : MonoBehaviour
{
  public bool triggered = false;
    private void OnTriggerEnter(Collider other)
    {
        triggered = true;
    }
    private void OnTriggerExit(Collider other)
    {
        triggered = false;
    }
}
