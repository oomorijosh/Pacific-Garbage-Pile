using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/* (C) 09/28/2024 Jason Leigh - Laboratory for Advanced Visualization & Applications
Set up eye separation for the two cameras.
*/

public class CC_CAMERA_SETUP : MonoBehaviour
{
    public GameObject leftCamera;
    public GameObject rightCamera;

    public CC_SETTINGS CCSettings;

    private Vector3 leftCamPosition, rightCamPosition;
    private bool currentStereo;
    private float currentEyeSeparation;

    // Start is called before the first frame update
    void Start()
    {
        if (!Application.isEditor){
            if (CC_CONFIG.LoadXMLConfig(CCSettings.ccConfigurationFile)) {
                CCSettings.eyeSeparation = CC_CONFIG.interaxial;
                CCSettings.invertStereo = CC_CONFIG.invertStereo;
                transform.localPosition = CC_CONFIG.headPosition;
            }
        }

        // Keep the authored eye positions relative to CC_HEAD so navigation
        // and live stereo changes preserve the rig's current position and rotation.
        leftCamPosition = leftCamera.transform.localPosition;
        rightCamPosition = rightCamera.transform.localPosition;
        ApplyEyeSeparation();
    }

    // Update is called once per frame
    void Update()
    {
        if ((currentStereo == CCSettings.invertStereo) && (currentEyeSeparation == CCSettings.eyeSeparation)) return;
        ApplyEyeSeparation();
    }

    void ApplyEyeSeparation()
    {
        currentStereo = CCSettings.invertStereo;
        currentEyeSeparation = CCSettings.eyeSeparation;
        float halfSeparation = currentEyeSeparation * 0.5f;
        if (currentStereo) halfSeparation = -halfSeparation;
        Vector3 eyeOffset = new Vector3(halfSeparation, 0f, 0f);
        leftCamera.transform.localPosition = leftCamPosition - eyeOffset;
        rightCamera.transform.localPosition = rightCamPosition + eyeOffset;
    }
}
