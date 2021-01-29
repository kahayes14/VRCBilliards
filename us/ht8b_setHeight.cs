
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ht8b_setHeight : UdonSharpBehaviour
{
    public GameObject HeightCalibrator;
    public Material _Material;
    void Start()
    {
        _Material.SetFloat("_ShadowOffset", HeightCalibrator.transform.position.y);
    }
}
