using UnityEngine;

public class TEIHandTrackingDotsUI : MonoBehaviour
{
    [SerializeField] private TEIHandTrackingFilter filter;
    [SerializeField] private RectTransform canvasRect;
    [SerializeField] private RectTransform leftDot;
    [SerializeField] private RectTransform rightDot;

    void Update()
    {
        UpdateDot(leftDot, filter.HasLeftHand, filter.LeftHand);
        UpdateDot(rightDot, filter.HasRightHand, filter.RightHand);
    }

    void UpdateDot(RectTransform dot, bool active, Vector2 normalized)
    {
        dot.gameObject.SetActive(active);
        if (!active) return;

        float x = (normalized.x - 0.5f) * canvasRect.rect.width;
        float y = (normalized.y - 0.5f) * canvasRect.rect.height;

        dot.anchoredPosition = new Vector2(x, -y);
    }
}