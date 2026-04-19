using UnityEngine;

public class TEIHandTrackingFilter : MonoBehaviour
{
    [Header("Input Source")]
    [SerializeField] private TEIHandTrackingRunner runner;

    [Header("Motion Filtering")]
    [SerializeField] private float smoothing = 12f;

    [SerializeField] private float handHoldTime = 0.5f;

    public bool HasLeftHand { get; private set; }
    public bool HasRightHand { get; private set; }

    public Vector2 LeftHand { get; private set; }
    public Vector2 RightHand { get; private set; }

    // Smoothed apparent hand-size proxy
    public float LeftHandScale { get; private set; }
    public float RightHandScale { get; private set; }

    private float leftLostTimer;
    private float rightLostTimer;

    void Update()
    {
        if (runner == null) return;

        // LEFT HAND
        if (runner.HasLeftHand)
        {
            LeftHand = Vector2.Lerp(LeftHand, runner.LeftHandCenter, smoothing * Time.deltaTime);
            LeftHandScale = Mathf.Lerp(LeftHandScale, runner.LeftHandScale, smoothing * Time.deltaTime);

            HasLeftHand = true;
            leftLostTimer = handHoldTime;
        }
        else
        {
            leftLostTimer -= Time.deltaTime;
            if (leftLostTimer <= 0f)
            {
                HasLeftHand = false;
            }
        }

        // RIGHT HAND
        if (runner.HasRightHand)
        {
            RightHand = Vector2.Lerp(RightHand, runner.RightHandCenter, smoothing * Time.deltaTime);
            RightHandScale = Mathf.Lerp(RightHandScale, runner.RightHandScale, smoothing * Time.deltaTime);

            HasRightHand = true;
            rightLostTimer = handHoldTime;
        }
        else
        {
            rightLostTimer -= Time.deltaTime;
            if (rightLostTimer <= 0f)
            {
                HasRightHand = false;
            }
        }
    }
}