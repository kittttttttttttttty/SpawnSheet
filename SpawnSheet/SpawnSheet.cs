﻿using SpawnSheet.Menus;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Newtonsoft.Json;
using ReLogic.Content;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using Terraria;
using Terraria.Chat;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

// TODO: move windows below inventory
// TODO: Filter recipes with unobtainables.
// TODO: debugmode, stat menu (from CMM)

// netMode 0 single player, netMode 1 multiplayer, netMode 2 Server
// netMode type 21 sync Main.item

namespace SpawnSheet
{
	internal class SpawnSheet : Mod
	{
		internal static ModKeybind ToggleSpawnSheetHotbarHotKey;
		internal static SpawnSheet instance;
		//internal static Dictionary<string, ModTranslation> translations; // reference to private field.
		internal Hotbar hotbar;
		internal ItemBrowser itemBrowser;
		internal NPCBrowser npcBrowser;
		internal RecipeBrowserWindow recipeBrowser;
		internal EventManagerHotbar eventManagerHotbar;

		internal Dictionary<string, bool> herosPermissions = new Dictionary<string, bool>();
		internal const string ModifySpawnRateMultiplier_Permission = "ModifySpawnRateMultiplier";
		internal const string ModifySpawnRateMultiplier_Display = "Modify Spawn Rate Multiplier";
		internal const string RecipeBrowser_Permission = "RecipeBrowser";
		internal const string RecipeBrowser_Display = "Recipe Browser";

		internal const int DefaultNumberOnlineToLoad = 30;
		public int numberOnlineToLoad = 0;

		public SpawnSheet() {
		}

		// to do: debugmode, stat

		public override void Load() {
			// Since we are using hooks not in older versions, and since ItemID.Count changed, we need to do this.
			if (BuildInfo.tMLVersion < new Version(0, 11, 5)) {
				throw new Exception("\nThis mod uses functionality only present in the latest tModLoader. Please update tModLoader to use this mod\n\n");
			}
			instance = this;

			ButtonClicked.Clear();
			ButtonTexture.Clear();
			ButtonTooltip.Clear();

			ToggleSpawnSheetHotbarHotKey = KeybindLoader.RegisterKeybind(this, "ToggleSpawnSheetHotbar", "K");

			if (Main.rand == null) {
				Main.rand = new Terraria.Utilities.UnifiedRandom();
			}

			//FieldInfo translationsField = typeof(LocalizationLoader).GetField("translations", BindingFlags.Static | BindingFlags.NonPublic);
			//translations = (Dictionary<string, ModTranslation>)translationsField.GetValue(this);
			//LoadTranslations();

			// set all to true on load
			herosPermissions[ModifySpawnRateMultiplier_Permission] = true;
			herosPermissions[RecipeBrowser_Permission] = true;
		}

		public override void Unload() {
			ButtonClicked.Clear();
			ButtonTexture.Clear();
			ButtonTooltip.Clear();

			AllItemsMenu.singleSlotArray = null;
			UI.UICheckbox.checkboxTexture = null;
			UI.UICheckbox.checkmarkTexture = null;
			UI.UIScrollBar.ScrollbarTexture = null;
			UI.UIScrollView.ScrollbgTexture = null;
			UI.UITextbox.textboxBackground = null;
			//UI.UIView.closeTexture = null;
			ItemBrowser.UnloadStatic();
			NPCBrowser.UnloadStatic();
			RecipeBrowserWindow.UnloadStatic();
			if (itemBrowser != null)
				itemBrowser.itemView = null;
			itemBrowser = null;
			npcBrowser = null;
			recipeBrowser = null;
			if (hotbar != null) {
				hotbar.buttonView?.RemoveAllChildren();
				hotbar.buttonView = null;
				hotbar = null;
			}
			instance = null;
			ToggleSpawnSheetHotbarHotKey = null;
			RecipeBrowserWindow.recipeView = null;
			RecipeBrowserWindow.lookupItemSlot = null;
			Hotbar.loginTexture = null;
			Hotbar.logoutTexture = null;
			SpawnRateMultiplier.button = null;
		}

		internal static string CSText(string category, string key) {
			return Language.GetTextValue($"Mods.SpawnSheet.{category}.{key}");
			//return translations[$"Mods.CheatSheet.{category}.{key}"].GetTranslation(Language.ActiveCulture);
			// This isn't good until after load....can revert after fixing static initializers for string[]
			// return Language.GetTextValue($"Mods.CheatSheet.{category}.{key}");
		}

		/*
		private void LoadTranslations()
		{
			var modTranslationDictionary = new Dictionary<string, ModTranslation>();

			var translationFiles = new List<string>();
			foreach (var item in File)
			{
				if (item.Key.StartsWith("Localization"))
					translationFiles.Add(item.Key);
			}
			foreach (var translationFile in translationFiles)
			{
				string translationFileContents = System.Text.Encoding.UTF8.GetString(GetFileBytes(translationFile));
				GameCulture culture = GameCulture.FromName(Path.GetFileNameWithoutExtension(translationFile));
				Dictionary<string, Dictionary<string, string>> dictionary = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(translationFileContents);
				foreach (KeyValuePair<string, Dictionary<string, string>> category in dictionary)
					foreach (KeyValuePair<string, string> kvp in category.Value)
					{
						ModTranslation mt;
						string key = category.Key + "." + kvp.Key;
						if (!modTranslationDictionary.TryGetValue(key, out mt))
							modTranslationDictionary[key] = mt = CreateTranslation(key);
						mt.AddTranslation(culture, kvp.Value);
					}
			}

			foreach (var value in modTranslationDictionary.Values)
			{
				AddTranslation(value);
			}
		}
		*/

		//public override void PreSaveAndQuit()
		//{
		//	SpawnRateMultiplier.HasPermission = true;
		//	CheatSheet.instance.hotbar.ChangedConfiguration();
		//}

		public override void PostSetupContent() {
			ConfigurationLoader.Initialized();
			try {
				if (ModLoader.TryGetMod("HEROsMod", out Mod herosMod)) {
					SetupHEROsModIntegration(herosMod);
				}
			}
			catch (Exception e) {
				Logger.Error("SpawnSheet->HEROsMod PostSetupContent Error: " + e.StackTrace + e.Message);
			}
		}

		private void SetupHEROsModIntegration(Mod herosMod) {
			// Add Permissions always.
			herosMod.Call(
				// Special string
				"AddPermission",
				// Permission Name
				ModifySpawnRateMultiplier_Permission,
				// Permission Display Name
				ModifySpawnRateMultiplier_Display
			);

			// Add Buttons only to non-servers (otherwise the server will crash, since textures aren't loaded on servers)
			if (!Main.dedServ) {
				herosMod.Call(
					// Special string
					"AddSimpleButton",
					// Name of Permission governing the availability of the button/tool
					ModifySpawnRateMultiplier_Permission,
					// Texture of the button. 38x38 is recommended for HERO's Mod. Also, a white outline on the icon similar to the other icons will look good.
					ModUtils.GetItemTexture(ItemID.WaterCandle),
					// A method that will be called when the button is clicked
					(Action)SpawnRateMultiplier.HEROsButtonPressed,
					// A method that will be called when the player's permissions have changed
					(Action<bool>)SpawnRateMultiplier.HEROsPermissionChanged,
					// A method that will be called when the button is hovered, returning the Tooltip
					(Func<string>)SpawnRateMultiplier.HEROsTooltip
				);
			}

			// Other non-tutorial permissions.
			// For simplicity, not doing buttons in Heros, just permissions for most tools.
			// Could implement most without sub-menus as buttons if I have time. Right and left click support in Heros desireable.
			var permissions = new List<ValueTuple<string, string>>() {
				(RecipeBrowser_Permission, RecipeBrowser_Display),
			};
			foreach (var permission in permissions) {
				herosMod.Call("AddPermission", permission.Item1, permission.Item2, (Action<bool>)((hasPermission) => HEROsPermissionChanged(permission.Item1, hasPermission)));
			}
		}

		public void HEROsPermissionChanged(string permission, bool hasPermission) {
			herosPermissions[permission] = hasPermission;
			// This is called a bunch at once, a little wasteful.
			SpawnSheet.instance.hotbar.ChangedConfiguration();
		}

		public void SetupUI() {
			//System.Collections.Concurrent.ConcurrentQueue<Action> glQueue = (System.Collections.Concurrent.ConcurrentQueue<Action>)typeof(Terraria.ModLoader.Engine.GLCallLocker).GetField("actionQueue", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
			//glQueue.Enqueue(() =>
			//{
			if (!Main.dedServ) {
				//for (int i = 0; i < ItemLoader.ItemCount; i++)
				//{
				//	Main.instance.LoadItem(i);
				//}

				try {
					ItemBrowser.LoadStatic();
					itemBrowser = new ItemBrowser(this);
					itemBrowser.SetDefaultPosition(new Vector2(80, 300));
					itemBrowser.Visible = false;

					NPCBrowser.LoadStatic();
					npcBrowser = new NPCBrowser(this);
					npcBrowser.SetDefaultPosition(new Vector2(30, 180));
					npcBrowser.Visible = false;

					RecipeBrowserWindow.LoadStatic();
					recipeBrowser = new RecipeBrowserWindow(this);
					recipeBrowser.SetDefaultPosition(new Vector2(30, 180));
					recipeBrowser.Visible = false;

					//eventManagerHotbar = new EventManagerHotbar(this);
					//eventManagerHotbar.Visible = false;
					//eventManagerHotbar.Hide();

					hotbar = new Hotbar(this);
					//hotbar.Position = new Microsoft.Xna.Framework.Vector2(120, 180);
					hotbar.Visible = true;
					if (!ModContent.GetInstance<SpawnSheetClientConfig>().HotbarShownByDefault)
						hotbar.Hide();
					else
						hotbar.Show();
				}
				catch (Exception e) {
					Logger.Error(e.ToString());
				}
			}
			//});
		}

		public static bool IsPlayerLocalServerOwner(Player player) {
			if (Main.netMode == 1) {
				return Netplay.Connection.Socket.GetRemoteAddress().IsLocalHost();
			}

			for (int plr = 0; plr < Main.maxPlayers; plr++)
				if (Netplay.Clients[plr].State == 10 && Main.player[plr] == player && Netplay.Clients[plr].Socket.GetRemoteAddress().IsLocalHost())
					return true;
			return false;
		}

		//public override void PostDrawInterface(SpriteBatch spriteBatch)
		//{
		//	//Main.spriteBatch.DrawString(FontAssets.MouseText.Value, "Drawn Always", new Vector2(Main.screenWidth/2, Main.screenHeight/2 + 20), Color.Aquamarine, 0.0f, new Vector2(), 0.8f, SpriteEffects.None, 0.0f);
		//	AllItemsMenu menu = (AllItemsMenu)this.GetGlobalItem("AllItemsMenu");
		//	menu.DrawUpdateAll(spriteBatch);
		//}

		//public override void PostUpdateInput()
		//{
		//	if (!Main.gameMenu)
		//	{
		//		//UIView.UpdateUpdateInput();
		//		AllItemsMenu menu = (AllItemsMenu)this.GetGlobalItem("AllItemsMenu");
		//		menu.UpdateInput();
		//	}
		//}

		//public override void UpdateMusic(ref int music)
		//{
		//	PreviousKeyState = Main.keyState;
		//}

		private KeyboardState PreviousKeyState;

		public void RegisterButton(Asset<Texture2D> texture, Action buttonClickedAction, Func<string> tooltip) {
			ButtonClicked.Add(buttonClickedAction);
			ButtonTexture.Add(texture);
			ButtonTooltip.Add(tooltip);
			//ErrorLogger.Log("1 "+ButtonClicked.Count);
			//ErrorLogger.Log("2 "+ ButtonTexture.Count);
			//ErrorLogger.Log("3 "+ ButtonTooltip.Count);
		}

		internal static List<Action> ButtonClicked = new List<Action>();
		internal static List<Asset<Texture2D>> ButtonTexture = new List<Asset<Texture2D>>();
		internal static List<Func<string>> ButtonTooltip = new List<Func<string>>();

		public override object Call(params object[] args) {
			try {
				string message = args[0] as string;
				if (message == "AddButton_Test") {
					Logger.Info("Button Adding...");
					RegisterButton(args[1] as Asset<Texture2D>, args[2] as Action, args[3] as Func<string>);
					Logger.Info("...Button Added");
				}
				else if (message == "HideHotbar") {
					hotbar.Hide();
				}
				else {
					Logger.Error("Call Error: Unknown Message: " + message);
				}
			}
			catch (Exception e) {
				Logger.Error("Call Error: " + e.StackTrace + e.Message);
			}
			return null;
		}

		public override void HandlePacket(BinaryReader reader, int whoAmI) {
			SpawnSheetMessageType msgType = (SpawnSheetMessageType)reader.ReadByte();
			string key;

			switch (msgType) {
				case SpawnSheetMessageType.SpawnNPC:
					int npcType = reader.ReadInt32();
					int netID = reader.ReadInt32();
					NPCSlot.HandleNPC(npcType, netID, true, whoAmI);
					key = "Mods.SpawnSheet.MobBrowser.SpawnNPCNotification";
					ChatHelper.BroadcastChatMessage(NetworkText.FromKey(key, netID, Netplay.Clients[whoAmI].Name), Color.Azure);
					//message = "Spawned " + netID + " by " + Netplay.Clients[whoAmI].Name;
					//NetMessage.SendData(25, -1, -1, message, 255, Color.Azure.R, Color.Azure.G, Color.Azure.B, 0);
					break;

				case SpawnSheetMessageType.SetSpawnRate:
					SpawnRateMultiplier.HandleSetSpawnRate(reader, whoAmI);
					break;

				case SpawnSheetMessageType.SpawnRateSet:
					SpawnRateMultiplier.HandleSpawnRateSet(reader, whoAmI);
					break;

				case SpawnSheetMessageType.RequestFilterNPC:
					int netID2 = reader.ReadInt32();
					bool desired = reader.ReadBoolean();
					NPCBrowser.FilterNPC(netID2, desired);
					ConfigurationLoader.SaveSetting();

					var packet = GetPacket();
					packet.Write((byte)SpawnSheetMessageType.InformFilterNPC);
					packet.Write(netID2);
					packet.Write(desired);
					packet.Send();
					break;

				case SpawnSheetMessageType.InformFilterNPC:
					int netID3 = reader.ReadInt32();
					bool desired2 = reader.ReadBoolean();
					NPCBrowser.FilterNPC(netID3, desired2);
					NPCBrowser.needsUpdate = true;
					break;
				//case CheatSheetMessageType.RequestToggleNPCSpawn:
				//	NPCSlot.HandleFilterRequest(reader.ReadInt32(), reader.ReadInt32(), true);
				//	break;
				default:
					Logger.Warn("Unknown Message type: " + msgType);
					break;
			}
		}

		public static Rectangle GetClippingRectangle(SpriteBatch spriteBatch, Rectangle r) {
			//Vector2 vector = new Vector2(this._innerDimensions.X, this._innerDimensions.Y);
			//Vector2 position = new Vector2(this._innerDimensions.Width, this._innerDimensions.Height) + vector;
			Vector2 vector = new Vector2(r.X, r.Y);
			Vector2 position = new Vector2(r.Width, r.Height) + vector;
			vector = Vector2.Transform(vector, Main.UIScaleMatrix);
			position = Vector2.Transform(position, Main.UIScaleMatrix);
			Rectangle result = new Rectangle((int)vector.X, (int)vector.Y, (int)(position.X - vector.X), (int)(position.Y - vector.Y));
			int width = spriteBatch.GraphicsDevice.Viewport.Width;
			int height = spriteBatch.GraphicsDevice.Viewport.Height;
			result.X = Utils.Clamp<int>(result.X, 0, width);
			result.Y = Utils.Clamp<int>(result.Y, 0, height);
			result.Width = Utils.Clamp<int>(result.Width, 0, width - result.X);
			result.Height = Utils.Clamp<int>(result.Height, 0, height - result.Y);
			return result;
		}
	}

	public static class SpawnSheetInterface
	{
		public static void RegisterButton(Asset<Texture2D> texture, Action buttonClickedAction, Func<string> tooltip) {
			if (!Main.dedServ) {
				ModContent.GetInstance<SpawnSheet>().RegisterButton(texture, buttonClickedAction, tooltip);
			}
		}

		public static void RegisterButton(SpawnSheetButton csb) {
			if (!Main.dedServ) {
				ModContent.GetInstance<SpawnSheet>().RegisterButton(csb.texture, csb.buttonClickedAction, csb.tooltip);
			}
		}
	}

	public class SpawnSheetButton
	{
		internal Asset<Texture2D> texture;

		//internal Action buttonClickedAction;
		//internal Func<string> tooltip;
		public SpawnSheetButton(Asset<Texture2D> texture/*, Action buttonClickedAction, Func<string> tooltip*/) {
			this.texture = texture;
			//	this.buttonClickedAction = buttonClickedAction;
			//	this.tooltip = tooltip;
		}

		public virtual void buttonClickedAction() {
		}

		public virtual string tooltip() {
			return "";
		}
	}

	internal enum SpawnSheetMessageType : byte
	{
		SpawnNPC,
		SetSpawnRate,
		SpawnRateSet,
		FilterNPC,
		RequestToggleNPCSpawn,
		RequestFilterNPC,
		InformFilterNPC,
	}

	static class SpawnSheetUtilities
	{
		private static Uri reporturl = new Uri("http://javid.ddns.net/tModLoader/jopojellymods/report.php");

		internal static void ReportException(Exception e) {
			SpawnSheet.instance.Logger.Error("SpawnSheet: " + e.Message + e.StackTrace);
			try {
				ReportData data = new ReportData(e);
				data.additionaldata = "Loaded Mods: " + string.Join(", ", ModLoader.Mods.Select(m => m.Name).ToArray());
				string jsonpayload = JsonConvert.SerializeObject(data);
				using (WebClient client = new WebClient()) {
					var values = new NameValueCollection
					{
						{ "jsonpayload", jsonpayload },
					};
					client.UploadValuesAsync(reporturl, "POST", values);
				}
			}
			catch { }
		}

		class ReportData
		{
			public string mod;
			public string modversion;
			public string tmodversion;
			public string platform;
			public string errormessage;
			public string additionaldata;

			public ReportData() {
				tmodversion = BuildInfo.tMLVersion.ToString();
				modversion = SpawnSheet.instance.Version.ToString();
				mod = "SpawnSheet";
				platform = ModLoader.CompressedPlatformRepresentation;
			}

			public ReportData(Exception e) : this() {
				errormessage = e.Message + e.StackTrace;
			}
		}
	}
}