
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ht8b_set4b : UdonSharpBehaviour
{

[SerializeField] ht8b target;
[SerializeField] bool kr;

void Interact()
{
	if( kr )
	{
		target._tr_sagu();
	}
	else
	{
		target._tr_yotsudama();
	}
}
}
