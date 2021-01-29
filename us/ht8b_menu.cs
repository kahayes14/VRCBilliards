#define COMPILE_WITH_TESTS
#define ALLOW_1P_AS_2P

// Auth lobby: Each player is required to register into the game before it begins
#define USE_AUTH_LOBBY

// King lobby: Anyone can mess with anything, and the network owner of ht8b script object
//   is responsible for sending out updates
//#define USE_KING_LOBBY

using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;
using System;

public class ht8b_menu : UdonSharpBehaviour{

const string FRP_LOW =  "<color=\"#ADADAD\">";
const string FRP_ERR =  "<color=\"#B84139\">";
const string FRP_WARN = "<color=\"#DEC521\">";
const string FRP_YES =  "<color=\"#69D128\">";
const string FRP_END =  "</color>";

// UI materials
[SerializeField] Material mat_high;
[SerializeField] Material mat_low;

// UI element objects
[SerializeField] GameObject loc_main;

[SerializeField] MeshRenderer[]  ui_gamemodes;
[SerializeField] MeshRenderer[]  ui_timelimits;
[SerializeField] MeshRenderer[]  ui_joinbuttons;
[SerializeField] MeshRenderer    ui_start;
[SerializeField] GameObject      ui_newGame;
[SerializeField] GameObject[]    ui_colour_selecters;
[SerializeField] MeshRenderer[]  ui_teambuttons;
[SerializeField] Text[]          ui_textplayers;
[SerializeField] BoxCollider[]   lobby_owner_only;

// Networking stuff
[SerializeField] GameObject[]    gm_tokens;
[SerializeField] ht8b            main;

// Visual
[SerializeField] SkinnedMeshRenderer mr_sand_timer;

// Localize from ht8b.cs
Texture[]      ball_textures;
GameObject[]   ball_renderers;
Material       ball_material;
Material       table_material;
GameObject     scorecard;

// Networked
[HideInInspector] public uint gamemode_id;
[HideInInspector] public uint colour_id;
[HideInInspector] public uint timer_id;
[HideInInspector] public uint teams_allowed;

// Non-critical
uint menu_loc;    // 0: NewGame button, 1: Main menu

[HideInInspector] public bool game_is_running = false;

#if USE_AUTH_LOBBY

VRCPlayerApi[] player_apis = new VRCPlayerApi[4];
bool[] player_ready        = new bool[4];

// 
int local_playerid = -1;      // -1: not joined, 0-3: joined as ID

#endif

Vector3[] reset_positions = new Vector3[ 16 ];

// Reset to fully defined state
public void _internal_state_reset()
{
   gamemode_id = 0u;
   colour_id = 0;
   timer_id = 0;
   teams_allowed = 0;
   menu_loc = 0;              // TODO: This should be 0
   game_is_running = false;
   
   #if USE_AUTH_LOBBY

   local_playerid = -1;

   for( int i = 0; i < 4; i ++ )
   {
      player_ready[ i ] = false;
      player_apis[ i ] = Networking.GetOwner( gm_tokens[ i ] );
   }

   #endif

   _menu_view();
}

Color k_fabricColour_gray = new Color( 0.3f, 0.3f, 0.3f, 1.0f );
Color k_fabricColour_red = new Color( 0.8962264f, 0.2081864f, 0.1310519f );
Color k_fabricColour_blue = new Color( 0.1f, 0.6f, 1.0f, 1.0f );
Color k_fabricColour_white = new Color( 0.8f, 0.8f, 0.8f, 1.0f );

Color k_lightColour_black = new Color( 0.01f, 0.01f, 0.01f, 1.0f );

[HideInInspector] public Color table_src_colour;   // Cloth color
Color table_current_colour;
[HideInInspector] public Color table_src_light;    // Light color
Color table_light_colour;

// VFX stuff

float colourchange_timer = 9.0f;
const float k_COLOUR_TRANSITION = 0.1f;
const float k_COLOUR_TRANS_RECIP = 10.0f;

bool has_transitioned = false;

float sand_timer_target_weight = 0.0f;
float sand_timer_weight = 0.0f;

public bool is_in_game = false;
public bool is_lobby_leader = false;

// Networking msg id shite
const byte k_7b_join = 0x00;
const byte k_7b_leave = 0x10;
const byte k_7b_nj_players = 0x20;  // Special new-joiner event to catch up quicker

const byte k_7b_menu_loc = 0x30;
const byte k_7b_ball_col = 0x40;
const byte k_7b_gamemode = 0x50;
const byte k_7b_timelimit = 0x60;
const byte k_7b_teams = 0x70;

uint _colour_max = 2u;

#if USE_AUTH_LOBBY
public void _team_view()
{
   _frp( FRP_LOW + "_team_view()" + FRP_END );

   if( teams_allowed == 0 )
   {
      ui_joinbuttons[ 2 ].gameObject.SetActive( false );
      ui_joinbuttons[ 3 ].gameObject.SetActive( false );

      ui_teambuttons[ 0 ].sharedMaterial = mat_low;
      ui_teambuttons[ 1 ].sharedMaterial = mat_high;
   }
   else
   {
      ui_joinbuttons[ 2 ].gameObject.SetActive( true );
      ui_joinbuttons[ 3 ].gameObject.SetActive( true );

      ui_teambuttons[ 0 ].sharedMaterial = mat_high;
      ui_teambuttons[ 1 ].sharedMaterial = mat_low;
   }
}
#endif

public void _menu_view()
{
   _frp( FRP_LOW + "_menu_view()" + FRP_END );

   if( menu_loc == 0 )
   {
      ui_newGame.SetActive( true );
      loc_main.SetActive( false );
   }
   else
   {
      scorecard.SetActive( false );
      ui_newGame.SetActive( false );
      loc_main.SetActive( true );

      //table_material.SetColor( "_EmissionColour", k_lightColour_black );
      table_src_light = k_lightColour_black;

      // Run view states when we load this menu
      _colours_view();
      _gamemode_view();
      _timelimit_view();

      #if USE_AUTH_LOBBY

      _team_view();
      _players_view();

      // Add controls for lobby master
      if( local_playerid == 0 )
      {
         for( int i = 0; i < lobby_owner_only.Length; i ++ )
         {
            lobby_owner_only[ i ].enabled = true;
         }
      }
      else // Or not
      {
         for( int i = 0; i < lobby_owner_only.Length; i ++ )
         {
            lobby_owner_only[ i ].enabled = false;
         }
      }

      for( int i = 0; i < 16; i ++ )
      {
         ball_renderers[ i ].transform.position = reset_positions[ i ];
      }

      #endif
   }
}

void _colours_view()
{
   _frp( FRP_LOW + "_colours_view()" + FRP_END );

   if( colourchange_timer > k_COLOUR_TRANSITION )
   {
      colourchange_timer = k_COLOUR_TRANSITION;
   }

   if( colourchange_timer < 0.0f )
   {
      colourchange_timer = -colourchange_timer;
   }

   has_transitioned = false;
}

void _gamemode_view()
{
   _frp( FRP_LOW + "_gamemode_view()" + FRP_END );

   for( uint i = 0; i < ui_gamemodes.Length; i ++ )
   {
      if( i == gamemode_id )
      {
         ui_gamemodes[ i ].sharedMaterial = mat_high; 
      }
      else
      {
         ui_gamemodes[ i ].sharedMaterial = mat_low;
      }
   }

   bool view_colour_selecters = true;

   if( gamemode_id == 1u ) // 9 ball
   {
      table_src_colour = k_fabricColour_blue;

      view_colour_selecters = false;
   }
   else // 8 ball derivatives
   {
      table_src_colour = k_fabricColour_gray;
   }

   if( gamemode_id == 2u )
   {
      _colour_max = 3u;
   }
   else
   {
      _colour_max = 2u;
   }

   ui_colour_selecters[ 0 ].SetActive( view_colour_selecters );
   ui_colour_selecters[ 1 ].SetActive( view_colour_selecters );
}

public void _timelimit_view()
{
   _frp( FRP_LOW + "_timelimit_view()" + FRP_END );

   for( int i = 0; i < ui_timelimits.Length; i ++ )
   {
      if( i == timer_id )
      {
         ui_timelimits[ i ].sharedMaterial = mat_high;
      }
      else
      {
         ui_timelimits[ i ].sharedMaterial = mat_low;
      }
   }

   // TODO: Sandtimer vars
   
   if( timer_id == 0 )
   {
      mr_sand_timer.enabled = false;
   }
   else
   {
      mr_sand_timer.enabled = true;

      if( timer_id == 1 )
      {
         sand_timer_target_weight = 50.0f;
      }
      else
      {
         sand_timer_target_weight = 0.0f;
      }
   }
}

#if USE_AUTH_LOBBY
void _players_view()
{
   // _frp( FRP_LOW + "_players_view()" + FRP_END );

   if( is_in_game )
   {
      _frp( FRP_ERR + "Menu was alive while game running for some reason... Disappearing" + FRP_END );
      this.gameObject.SetActive( false );
      return;
   }

   // Take most updated data
   _api_users_refresh();

   // Constantly internalize local state into ht8b.cs to make sure we can allow us to play
   // when system control is handed over to it
   main.local_playerid = local_playerid;

   ui_textplayers[ 0 ].text = "";
   ui_textplayers[ 1 ].text = "";

   uint readied_players = 0;

   // Texts for who is playing
   for( uint i = 0; i < (teams_allowed == 1? 4: 2); i ++ )
   {
      if( player_ready[ i ] )
      {
#if UNITY_EDITOR
         if( (i & 0x1U) == 1 )
         {
            ui_textplayers[ 1 ].text += "<UNITY_EDITOR>\n";

            readied_players |= 0x2u;
         }
         else
         {
            ui_textplayers[ 0 ].text = "\n<UNITY_EDITOR>" + ui_textplayers[ 0 ].text;

            readied_players |= 0x1u;
         }
#else
         string dispname = player_apis[ i ].displayName;

         if( i == local_playerid )
         {

            // Check for value stomping
            if( player_apis[ i ] != Networking.LocalPlayer )
            {
               _frp( FRP_ERR + "Local player value was stomped by a rogue network event" + FRP_END );
               local_playerid = -1; 
               return;
            }

            // Show local player in italics
            dispname = "<i>" + dispname + "</i>";
         }

         if( (i & 0x1U) == 1 )
         {
            ui_textplayers[ 1 ].text += dispname + "\n";

            readied_players |= 0x2;
         }
         else
         {
            ui_textplayers[ 0 ].text = "\n" + dispname + ui_textplayers[ 0 ].text;

            readied_players |= 0x1;
         }
#endif
      }

      // Update join buttons
      if( local_playerid >= 0 )
      {
         if( i == (uint)local_playerid )
         {
            ui_joinbuttons[ i ].gameObject.SetActive( true );
            ui_joinbuttons[ i ].sharedMaterial = mat_low;
         }
         else
         {
            ui_joinbuttons[ i ].gameObject.SetActive( false );
         }
      }
      else
      {
         if( player_ready[ i ] )
         {
            ui_joinbuttons[ i ].gameObject.SetActive( false );
         }
         else
         {
            ui_joinbuttons[ i ].gameObject.SetActive( true );
            ui_joinbuttons[ i ].sharedMaterial = mat_high;
         }
      }
   }

   if( (readied_players == 0x3u || gamemode_id == 2u) && local_playerid == 0 )
   {
      ui_start.sharedMaterial = mat_high;
      ui_start.GetComponent<BoxCollider>().enabled = true;
   }
   else
   {
      ui_start.sharedMaterial = mat_low;
      ui_start.GetComponent<BoxCollider>().enabled = false;
   }
}

void OnPlayerLeft( VRCPlayerApi player )
{
   // Lobby leader left so force a reset

   if( player == player_apis[ 0 ] )
   {
      _internal_state_reset();
   }
}

void OnPlayerJoined( VRCPlayerApi player )
{
   if( player == Networking.LocalPlayer )
      return;

   if( local_playerid == 0 )
   {
      // Send newjoiner update
      uint data = 0x00U;

      // Compress players arr
      for( int i = 0; i < 4; i ++ )
      {
         if( player_ready[ i ] )
            data |= 0x1U << i;
      }

      // Send which player IDs is joined
      _b7_send( (byte)(k_7b_nj_players | data) );

      // Send all the other states they need to catch up on
      _b7_send( (byte)( k_7b_menu_loc | menu_loc ) );
      _b7_send( (byte)( k_7b_ball_col | colour_id ) );
      _b7_send( (byte)( k_7b_teams | teams_allowed ) );
      _b7_send( (byte)( k_7b_timelimit | timer_id ) );
      _b7_send( (byte)( k_7b_gamemode | gamemode_id ) );
   }
}

#endif

#if USE_KING_LOBBY

void OnPlayerJoined( VRCPlayerApi player )
{
   // Ignore this if we are the one joining
   if( player == Networking.LocalPlayer )
      return;

   if( Networking.GetOwner( main.gameObject ) == Networking.LocalPlayer )
   {
      FRP( FRP_LOW + "Player joined, updating him on state" + FRP_END );

      // Send all the other states they need to catch up on
      _b7_send( (byte)( k_7b_menu_loc | menu_loc ) );
      _b7_send( (byte)( k_7b_ball_col | colour_id ) );
      _b7_send( (byte)( k_7b_timelimit | timer_id ) );
      _b7_send( (byte)( k_7b_gamemode | gamemode_id ) );
   }
}

#endif

public void Start()
{
   // getting inspector variables from ht8b.cs
   ball_textures = main.textureSets;
   ball_renderers = main.balls_render;
   ball_material = main.ballMaterial;
   table_material = main.tableMaterial;
   scorecard = main.scoreCardRenderer.gameObject;

   for( int i = 0; i < 16; i ++ )
   {
      reset_positions[ i ] = ball_renderers[ i ].transform.position;
   }

   // Initialize visual state to match internal
   _internal_state_reset();
}

void _transition_apex()
{
   ball_material.SetTexture( "_MainTex", ball_textures[ colour_id ] );

   for( int i = 0; i < 16; i ++ )
   {
      ball_renderers[ i ].transform.position = reset_positions[ i ];

      if( gamemode_id == 1u )
      {
         if( i >= 10 )
         {
            ball_renderers[ i ].SetActive( false );
         }
         else
         {
            ball_renderers[ i ].SetActive( true );
         }
      }
      else
      {
         ball_renderers[ i ].SetActive( true );
      }
   }

}

#if USE_AUTH_LOBBY
void _lobby_refresh()
{
   _players_view();
}
#endif

float last_check = 0.0f;
public void Update()
{
   #if USE_AUTH_LOBBY
   if( Time.timeSinceLevelLoad > last_check + 1.5f )
   {
      last_check = Time.timeSinceLevelLoad;
      _lobby_refresh();
   }
   #endif

   if( colourchange_timer <= k_COLOUR_TRANSITION )
   {
      colourchange_timer -= Time.deltaTime;

      if( colourchange_timer < 0.0f )
      {
         if( !has_transitioned )
         {
            _transition_apex();
            has_transitioned = true;
         }
      }

      float scaling = Mathf.Abs( colourchange_timer ) * k_COLOUR_TRANS_RECIP;

      if( colourchange_timer < -k_COLOUR_TRANSITION )
      {
         colourchange_timer = 9.0f;

         scaling = 1.0f;
      }

      for( int i = 0; i < 16; i ++ )
      {
         ball_renderers[ i ].transform.localScale = new Vector3( scaling, scaling, scaling );
      }
   }

   if( menu_loc == 0 )
   {
      table_light_colour = table_src_light * (Mathf.Sin( Time.timeSinceLevelLoad * 3.0f) * 0.5f + 1.0f);
   }
   else
   {
      table_light_colour = Color.Lerp( table_light_colour, table_src_light, Time.deltaTime * 5.0f );
      table_current_colour = Color.Lerp( table_current_colour, table_src_colour, Time.deltaTime * 5.0f );
      table_material.SetColor( "_ClothColour", table_current_colour );
   }

   table_material.SetColor( "_EmissionColour", new Color( table_light_colour.r, table_light_colour.g, table_light_colour.b, 0.0f ) );

   sand_timer_weight = Mathf.Lerp( sand_timer_weight, sand_timer_target_weight, Time.deltaTime * 5.0f );
   mr_sand_timer.SetBlendShapeWeight( 0, sand_timer_weight );
}


void _frp( string ln )
{
   Debug.Log( "[<color=\"#B5438F\">ht8b</color>] " + ln );
}

// MENU BUTTON INPUTS
// ===================================================================================================================================================================================
[HideInInspector] public int _in_joinas_id = 0;
public void _on_joinas()
{
#if USE_AUTH_LOBBY
   int player_count = 0;
   for( int i = 0; i < 4; i ++ )
   {
      if( player_ready[ i ] )
         player_count ++;
   }

   if( player_ready[ _in_joinas_id ] )
   {
      if( local_playerid == _in_joinas_id )
      {
         // Normal lobby leave
         _frp( FRP_WARN + "Leaving lobby as " + local_playerid + FRP_END );
         local_playerid = -1;

         // Networked leave
         _b7_send( (byte)(k_7b_leave | _in_joinas_id) );
      }
      else
      {
         //  Error Tried to join someone elses slot
#if UNITY_EDITOR
         _frp( FRP_ERR + "Tried to join as player " + _in_joinas_id + ", but player <UNITY_EDITOR> was already registered there" );
#else
         _frp( FRP_ERR + "Tried to join as player " + _in_joinas_id + ", but player " + Networking.GetOwner( gm_tokens[ _in_joinas_id ] ).displayName + " was already registered there" );
#endif
      }
   }
   else
   {
      if( local_playerid != -1 )
      {
         // Error join
         _frp( FRP_ERR + "Tried to join as player " + _in_joinas_id + ", but already in the game as " + local_playerid + ". UI is lagged." + FRP_END );
      }
      else
      {
         if( player_count == 0 && _in_joinas_id != 0 )
         {
            // Force first joiner to host
            _frp( FRP_WARN + "Switching to host automatically" + FRP_END );
            _in_joinas_id = 0;
         }

         // Normal lobby join
         _frp( FRP_YES + "Joining as " + _in_joinas_id + FRP_END );
         local_playerid = _in_joinas_id;

         // This is simply a hack for name transmission cause yeee
         Networking.SetOwner( Networking.LocalPlayer, gm_tokens[ local_playerid ] );

         // Networked join
         _b7_send( (byte)(k_7b_join | _in_joinas_id) );
      }
   }
#endif
}

// Change menu location button
[HideInInspector] public uint _in_menu_loc = 0;
public void _on_menu_change()
{
#if USE_AUTH_LOBBY
   // Only allow one way 0->1 change at the moment.
   if( local_playerid == 0 )
   {
#endif
      if( _in_menu_loc == 1 )
      {
         menu_loc = _in_menu_loc;

         // Networked menu change
         _b7_send( (byte)(k_7b_menu_loc | menu_loc) );
      }
      else
      {
         _frp( FRP_ERR + "Menu transitions other than 0->1 are not implemented" + FRP_END );
      }
#if USE_AUTH_LOBBY
   }
   else
   {
      _frp( FRP_ERR + "Cannot change menu state as we are not the lobby leader" + FRP_END );
   }
#endif
}

// Change colourset
[HideInInspector] public int _in_colourchange_dir = 0;
public void _on_colourchange()
{
#if USE_AUTH_LOBBY
   if( local_playerid == 0 )
   {
#endif
      int newcol = (int)colour_id + _in_colourchange_dir;

      if( newcol < 0 )
      {
         newcol = (int)_colour_max;
      }

      if( newcol > _colour_max )
      {
         newcol = 0;
      }

      colour_id = (uint)newcol;

      // Networked colour change
      _b7_send( (byte)( k_7b_ball_col | colour_id ) );

      // Local view
      _colours_view();
#if USE_AUTH_LOBBY
   }
   else
   {
      _frp( FRP_ERR + "Cannot change ball colours when not the lobby leader" + FRP_END );
   }
#endif
}

[HideInInspector] public int _in_allow_teams = 0;
public void _on_teamallowchange()
{
#if USE_AUTH_LOBBY
   if( local_playerid == 0 )
   {
#endif

      teams_allowed = (uint)_in_allow_teams;

      // Networked team allow
      _b7_send( (byte)( k_7b_teams | teams_allowed ) );

#if USE_AUTH_LOBBY
   }
   else
   {
      _frp( FRP_ERR + "Cannot change team settings if not the lobby leader" + FRP_END );
   }
#endif
}

[HideInInspector] public int _in_timelimitid = 0;
public void _on_timelimitchange()
{
#if USE_AUTH_LOBBY
   if( local_playerid == 0 )
   {
#endif

      timer_id = (uint)_in_timelimitid;

      // Networked timelimit update
      _b7_send( (byte)( k_7b_timelimit | timer_id ) );

#if USE_AUTH_LOBBY
   }
   else
   {
      _frp( FRP_ERR + "Cannot change time limit if not lobby leader" + FRP_END );
   }
#endif
}

[HideInInspector] public int _in_gamemodeid = 0;
public void _on_gamemode_change()
{
#if USE_AUTH_LOBBY
   if( local_playerid == 0 )
   {
#endif
      // If 9 ball, disable colour
      if( _in_gamemodeid == 1 )
      {
         colour_id = 3U;
         _colours_view();
         _b7_send( (byte)( k_7b_ball_col | colour_id ) );
      }
      else
      {
         // US is locked to gamemode 1 so we have to reset colour ID
         if( gamemode_id == 1u )
         {
            colour_id = 0U;
            _colours_view();
            _b7_send( (byte)( k_7b_ball_col | colour_id ) );
         }
      }

      gamemode_id = (uint)_in_gamemodeid;

      // Networked gamemode change
      _b7_send( (byte)( k_7b_gamemode | gamemode_id ) );

#if USE_AUTH_LOBBY
   }
   else
   {
      _frp( FRP_ERR + "Cannot change gamemode if not lobby leader" + FRP_END );
   }
#endif
}

// ==============================================================================================================================================================================

#if USE_AUTH_LOBBY

void _api_users_refresh()
{
   for( int i = 0; i < 4; i ++ )
   {
      player_apis[ i ] = Networking.GetOwner( gm_tokens[ i ] );
   }
}

#endif

// Send 7 bits over the network
void _b7_send( byte data )
{
   if( game_is_running )
   {
      _frp( FRP_ERR + "Tried to _b7_send while game was running" + FRP_END );
      return;
   }

   if( (data & 0x80U) > 0 )
   {
      _frp( FRP_ERR + "Tried to send more than 7 bits..." + FRP_END );

      return;
   }

#if UNITY_EDITOR
   this.SendCustomEvent( "B7" + data.ToString("X2") );
#else
   this.SendCustomNetworkEvent( VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "B7" + data.ToString("X2") );
#endif
}

// Recieve 7 bits from network
private void _b7_proc( byte data )
{
   if( !this.gameObject.activeSelf )
   {
      _frp( FRP_ERR + "Tried to run _b7_proc while this gameobject was disabled" + FRP_END );
      return;
   }

   uint msgid = data & 0x70U;

   if (msgid == k_7b_join)                                                 // EV 0x00: Player join
   {
#if USE_AUTH_LOBBY
      uint playerid = (data & 0x3U);

      _frp( FRP_YES + ".recv: Join player: " + playerid + FRP_END );
      player_ready[ playerid ] = true;

      _players_view();
#else

      _frp(FRP_ERR + ".recv: Join player. ht8b was compiled without auth lobby enabled" + FRP_END);

#endif
   } 
   else if (msgid == k_7b_leave)                                            // EV 0x01: Player leave
   {
#if USE_AUTH_LOBBY
      uint playerid = (data & 0x3U);

      _frp( FRP_WARN + ".recv: Leave player: " + playerid + FRP_END );
      
      if( playerid == 0x00 )
      {
         _frp( FRP_WARN + "Lobby leader left, resetting" + FRP_END );
         _internal_state_reset();
      }
      else
      {
         player_ready[ playerid ] = false;
      }
      _players_view();
#else
      _frp(FRP_ERR + ".recv: Leave player. ht8b was compiled without auth lobby enabled" + FRP_END);
#endif
   } 
   else if (msgid == k_7b_nj_players)                                          // EV 0x02: New join, playing status update
   {
#if USE_AUTH_LOBBY
      _frp( FRP_LOW + ".recv: Lobby playing status" + FRP_END );
      for( int i = 0; i < 4; i ++ )
      {
         player_ready[ i ] = ((data >> i) & 0x1) > 0;
      }

      _players_view();
#else
      _frp( FRP_ERR + ".recv: New join status. ht8b was compiled without auth lobby enabled" + FRP_END );
#endif
   }
   else if( msgid == k_7b_menu_loc )                                          // EV 0x03: Menu location change
   {
      _frp( FRP_LOW + ".recv: Menu change location" + FRP_END );

      menu_loc = data & 0xfu;
      _menu_view();
   }
   else if( msgid == k_7b_ball_col )                                          // EV 0x04: Ball colours
   {
      _frp( FRP_LOW + ".recv: Colour change" + FRP_END );

      uint newid = (data & 0x3U);
      if( newid != colour_id )
      {
         colour_id = newid;
         _colours_view();
      }
   }
   else if( msgid == k_7b_gamemode )                                          // Ev 0x05: Gamemode
   {
      _frp( FRP_LOW + ".recv: Gamemode change" + FRP_END );

      gamemode_id = data & 0x3u;

      _gamemode_view();
      #if USE_AUTH_LOBBY
      _players_view();
      #endif
   }
   else if( msgid == k_7b_timelimit )                                         // EV 0x06: Timelimit
   {
      _frp( FRP_LOW + ".recv: Timelimit change" + FRP_END );

      timer_id = data & 0x3U;

      if( timer_id == 3U )
      {
         _frp( FRP_ERR + "got timer ID 3, this is undefined. Something went wrong in network transmission" + FRP_END );
         timer_id = 2U;
      }

      _timelimit_view();
   }
   else if( msgid == k_7b_teams )                                             // EV 0x07: Teams
   {
      #if USE_AUTH_LOBBY
      _frp( FRP_LOW + ".recv: Team allow change" + FRP_END );

      teams_allowed = data & 0x1U;

      // Get out of the lobby!!! QUIIIIIIIIIIICK
      if( teams_allowed == 0 && local_playerid > 1 )
      {
         _frp( FRP_WARN + "We can't fit in this lobby anymore sadge" + FRP_END );
         _b7_send( (byte)( k_7b_leave | (uint)local_playerid ) );

         local_playerid = -1;
      }

      _team_view();
      _players_view();
      #else

      _frp( FRP_ERR + ".recv: New join status. ht8b was compiled without auth lobby enabled" + FRP_END );

      #endif
   }
   else
   {
      // This state will never be hit unless the solar system gets sucked into a black hole
      _frp( FRP_ERR + ".recv: Unkown message ID (" + msgid + ")" + FRP_END );
   }
}

// VRChat
public void B700(){ _b7_proc( 0x0 ); }
public void B701(){ _b7_proc( 0x1 ); }
public void B702(){ _b7_proc( 0x2 ); }
public void B703(){ _b7_proc( 0x3 ); }
public void B704(){ _b7_proc( 0x4 ); }
public void B705(){ _b7_proc( 0x5 ); }
public void B706(){ _b7_proc( 0x6 ); }
public void B707(){ _b7_proc( 0x7 ); }
public void B708(){ _b7_proc( 0x8 ); }
public void B709(){ _b7_proc( 0x9 ); }
public void B70A(){ _b7_proc( 0xa ); }
public void B70B(){ _b7_proc( 0xb ); }
public void B70C(){ _b7_proc( 0xc ); }
public void B70D(){ _b7_proc( 0xd ); }
public void B70E(){ _b7_proc( 0xe ); }
public void B70F(){ _b7_proc( 0xf ); }
public void B710(){ _b7_proc( 0x10 ); }
public void B711(){ _b7_proc( 0x11 ); }
public void B712(){ _b7_proc( 0x12 ); }
public void B713(){ _b7_proc( 0x13 ); }
public void B714(){ _b7_proc( 0x14 ); }
public void B715(){ _b7_proc( 0x15 ); }
public void B716(){ _b7_proc( 0x16 ); }
public void B717(){ _b7_proc( 0x17 ); }
public void B718(){ _b7_proc( 0x18 ); }
public void B719(){ _b7_proc( 0x19 ); }
public void B71A(){ _b7_proc( 0x1a ); }
public void B71B(){ _b7_proc( 0x1b ); }
public void B71C(){ _b7_proc( 0x1c ); }
public void B71D(){ _b7_proc( 0x1d ); }
public void B71E(){ _b7_proc( 0x1e ); }
public void B71F(){ _b7_proc( 0x1f ); }
public void B720(){ _b7_proc( 0x20 ); }
public void B721(){ _b7_proc( 0x21 ); }
public void B722(){ _b7_proc( 0x22 ); }
public void B723(){ _b7_proc( 0x23 ); }
public void B724(){ _b7_proc( 0x24 ); }
public void B725(){ _b7_proc( 0x25 ); }
public void B726(){ _b7_proc( 0x26 ); }
public void B727(){ _b7_proc( 0x27 ); }
public void B728(){ _b7_proc( 0x28 ); }
public void B729(){ _b7_proc( 0x29 ); }
public void B72A(){ _b7_proc( 0x2a ); }
public void B72B(){ _b7_proc( 0x2b ); }
public void B72C(){ _b7_proc( 0x2c ); }
public void B72D(){ _b7_proc( 0x2d ); }
public void B72E(){ _b7_proc( 0x2e ); }
public void B72F(){ _b7_proc( 0x2f ); }
public void B730(){ _b7_proc( 0x30 ); }
public void B731(){ _b7_proc( 0x31 ); }
public void B732(){ _b7_proc( 0x32 ); }
public void B733(){ _b7_proc( 0x33 ); }
public void B734(){ _b7_proc( 0x34 ); }
public void B735(){ _b7_proc( 0x35 ); }
public void B736(){ _b7_proc( 0x36 ); }
public void B737(){ _b7_proc( 0x37 ); }
public void B738(){ _b7_proc( 0x38 ); }
public void B739(){ _b7_proc( 0x39 ); }
public void B73A(){ _b7_proc( 0x3a ); }
public void B73B(){ _b7_proc( 0x3b ); }
public void B73C(){ _b7_proc( 0x3c ); }
public void B73D(){ _b7_proc( 0x3d ); }
public void B73E(){ _b7_proc( 0x3e ); }
public void B73F(){ _b7_proc( 0x3f ); }
public void B740(){ _b7_proc( 0x40 ); }
public void B741(){ _b7_proc( 0x41 ); }
public void B742(){ _b7_proc( 0x42 ); }
public void B743(){ _b7_proc( 0x43 ); }
public void B744(){ _b7_proc( 0x44 ); }
public void B745(){ _b7_proc( 0x45 ); }
public void B746(){ _b7_proc( 0x46 ); }
public void B747(){ _b7_proc( 0x47 ); }
public void B748(){ _b7_proc( 0x48 ); }
public void B749(){ _b7_proc( 0x49 ); }
public void B74A(){ _b7_proc( 0x4a ); }
public void B74B(){ _b7_proc( 0x4b ); }
public void B74C(){ _b7_proc( 0x4c ); }
public void B74D(){ _b7_proc( 0x4d ); }
public void B74E(){ _b7_proc( 0x4e ); }
public void B74F(){ _b7_proc( 0x4f ); }
public void B750(){ _b7_proc( 0x50 ); }
public void B751(){ _b7_proc( 0x51 ); }
public void B752(){ _b7_proc( 0x52 ); }
public void B753(){ _b7_proc( 0x53 ); }
public void B754(){ _b7_proc( 0x54 ); }
public void B755(){ _b7_proc( 0x55 ); }
public void B756(){ _b7_proc( 0x56 ); }
public void B757(){ _b7_proc( 0x57 ); }
public void B758(){ _b7_proc( 0x58 ); }
public void B759(){ _b7_proc( 0x59 ); }
public void B75A(){ _b7_proc( 0x5a ); }
public void B75B(){ _b7_proc( 0x5b ); }
public void B75C(){ _b7_proc( 0x5c ); }
public void B75D(){ _b7_proc( 0x5d ); }
public void B75E(){ _b7_proc( 0x5e ); }
public void B75F(){ _b7_proc( 0x5f ); }
public void B760(){ _b7_proc( 0x60 ); }
public void B761(){ _b7_proc( 0x61 ); }
public void B762(){ _b7_proc( 0x62 ); }
public void B763(){ _b7_proc( 0x63 ); }
public void B764(){ _b7_proc( 0x64 ); }
public void B765(){ _b7_proc( 0x65 ); }
public void B766(){ _b7_proc( 0x66 ); }
public void B767(){ _b7_proc( 0x67 ); }
public void B768(){ _b7_proc( 0x68 ); }
public void B769(){ _b7_proc( 0x69 ); }
public void B76A(){ _b7_proc( 0x6a ); }
public void B76B(){ _b7_proc( 0x6b ); }
public void B76C(){ _b7_proc( 0x6c ); }
public void B76D(){ _b7_proc( 0x6d ); }
public void B76E(){ _b7_proc( 0x6e ); }
public void B76F(){ _b7_proc( 0x6f ); }
public void B770(){ _b7_proc( 0x70 ); }
public void B771(){ _b7_proc( 0x71 ); }
public void B772(){ _b7_proc( 0x72 );}
public void B773(){ _b7_proc( 0x73 ); }
public void B774(){ _b7_proc( 0x74 ); }
public void B775(){ _b7_proc( 0x75 ); }
public void B776(){ _b7_proc( 0x76 ); }
public void B777(){ _b7_proc( 0x77 ); }
public void B778(){ _b7_proc( 0x78 ); }
public void B779(){ _b7_proc( 0x79 ); }
public void B77A(){ _b7_proc( 0x7a ); }
public void B77B(){ _b7_proc( 0x7b ); }
public void B77C(){ _b7_proc( 0x7c ); }
public void B77D(){ _b7_proc( 0x7d ); }
public void B77E(){ _b7_proc( 0x7e ); }
public void B77F(){ _b7_proc( 0x7f ); }

}