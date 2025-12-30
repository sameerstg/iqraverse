using StarterAssets;
using System.Collections.Generic;
using UnityEngine;

public class Trigger : MonoBehaviour
{
    public Transform mascot;
    public List<GameObject> toOff; 
    public List<GameObject> toOn;
  public bool triggered = false;
    private void OnTriggerEnter(Collider other)
    {
        triggered = true;
        var mascotPosition = mascot.position;
        FirstPersonController.Instance.CinemachineCameraTarget.transform.LookAt(mascotPosition,Vector3.up); 
        FirstPersonController.Instance.transform.position= new Vector3(transform.position.x, FirstPersonController.Instance.transform.position.y, transform.position.z);    
        mascot.Translate(Vector3.up * 50);
        mascot.gameObject.SetActive(true);
        LeanTween.move(mascot.gameObject, mascotPosition, 1f).setEaseOutBounce();
        FirstPersonController.Instance.enabled = false;
        foreach (var go in toOff)
        {
            go.SetActive(false);
        }
        foreach (var go in toOn)
        {
            go.SetActive(true);
        }
    }
    private void Update()
    {
        if(triggered && Input.GetKeyDown(KeyCode.Escape))
        {
            FirstPersonController.Instance.enabled = true;
            foreach (var go in toOff)
            {
                go.SetActive(true);
            }
            foreach (var go in toOn)
            {
                go.SetActive(false);
            }
            triggered = false;
            FirstPersonController.Instance.transform.Translate(Vector3.back * 5);
        }
    }
    private void OnTriggerExit(Collider other)
    {
        triggered = false;
    }
}
