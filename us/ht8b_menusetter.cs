#define COMPILE_WITH_TESTS

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ht8b_menusetter : UdonSharpBehaviour
{

[SerializeField] int colourSet = 0;
[SerializeField] int gameMode = -1;
[SerializeField] int timer = -1;
[SerializeField] int joinPlayer = -1;
[SerializeField] int menu_loc = -1;
[SerializeField] int allowTeams = -1;

[SerializeField] ht8b_menu menu;
[SerializeField] ht8b main;

[SerializeField] bool startGame = false;

#if COMPILE_WITH_TESTS
public bool forceInteract = false;
#endif

void Interact()
{
   if( colourSet != 0 )
   {
      menu._in_colourchange_dir = colourSet;
      menu._on_colourchange();
   }

   if( gameMode >= 0 )
   {
      menu._in_gamemodeid = gameMode;
      menu._on_gamemode_change();
   }

   if( timer >= 0 )
   {
      menu._in_timelimitid = timer;
      menu._on_timelimitchange();
   }

   if( startGame )
   {
      // This will disable menu
      main._tr_newgame();
   }

   if( allowTeams >= 0 )
   {
      menu._in_allow_teams = allowTeams;
      menu._on_teamallowchange();
   }

   if( joinPlayer >= 0 )
   {
      menu._in_joinas_id = joinPlayer;
      menu._on_joinas();
   }

   if( menu_loc >= 0 )
   {
      menu._in_menu_loc = (uint)menu_loc; 
      menu._on_menu_change();
   }
}

#if COMPILE_WITH_TESTS
void Update()
{
   if( forceInteract )
   {
      Interact();
      forceInteract = false;
   }
}
#endif

}
