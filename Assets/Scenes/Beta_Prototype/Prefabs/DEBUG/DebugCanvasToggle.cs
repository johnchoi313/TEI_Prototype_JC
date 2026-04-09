using UnityEngine;

public class DebugCanvasToggle : MonoBehaviour
{
    [SerializeField] private GameObject debugCanvasRoot;

    private void Start()
    {
        debugCanvasRoot.SetActive(false);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1) && debugCanvasRoot != null)
            debugCanvasRoot.SetActive(!debugCanvasRoot.activeSelf);
    }
}
