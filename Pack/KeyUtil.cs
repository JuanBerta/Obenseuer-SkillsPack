using System;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using BepInEx.Unity.Mono.Configuration;
using UnityEngine;

namespace SkillsPackMod
{
	/// <summary>
	/// Hotkey check that does not depend on the keyboard layout. Unity's old Input.GetKeyDown(KeyCode.B) follows the
	/// typed letter, so on a Russian (or other non-Latin) layout the key that is "B" on a US keyboard never fires.
	/// The new Input System reads the physical key position, which is what a key setting like B should mean.
	/// </summary>
	internal static class KeyUtil
	{
		private static bool inputSystemBroken;

		public static bool IsDown(KeyboardShortcut s)
		{
			try { if (s.IsDown()) return true; } catch (Exception) { }
			if (inputSystemBroken || s.MainKey == KeyCode.None) return false;
			try { return PhysicalDown(s); }
			catch (Exception) { inputSystemBroken = true; return false; }   // Input System missing in this build: legacy check only
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool PhysicalDown(KeyboardShortcut s)
		{
			var kb = UnityEngine.InputSystem.Keyboard.current;
			if (kb == null) return false;
			UnityEngine.InputSystem.Key key;
			if (!ToKey(s.MainKey, out key)) return false;
			if (!kb[key].wasPressedThisFrame) return false;
			foreach (KeyCode m in s.Modifiers)
			{
				UnityEngine.InputSystem.Key mk;
				if (!ToKey(m, out mk) || !kb[mk].isPressed) return false;
			}
			return true;
		}

		private static bool ToKey(KeyCode code, out UnityEngine.InputSystem.Key key)
		{
			string n = code.ToString();
			if (n.StartsWith("Alpha")) n = "Digit" + n.Substring(5);
			else if (n == "LeftShift" || n == "RightShift" || n == "LeftControl" || n == "RightControl" || n == "LeftAlt" || n == "RightAlt") { }
			else if (n == "Return") n = "Enter";
			return Enum.TryParse(n, out key);
		}

		/// <summary>Logs the keyboard layout so a "key does nothing" report can be checked against it.</summary>
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static string LayoutName()
		{
			try { var kb = UnityEngine.InputSystem.Keyboard.current; return kb != null ? kb.keyboardLayout : "no keyboard"; }
			catch (Exception) { return "unknown"; }
		}
	}
}
