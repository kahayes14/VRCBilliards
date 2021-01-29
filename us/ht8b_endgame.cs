
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ht8b_endgame : UdonSharpBehaviour
{

[SerializeField] ht8b main;

void Interact()
{
	EndGame();
}

public void OnButtonPressed()
{
	EndGame();
}

void EndGame()
{
	main._tr_force_end();
}

}
