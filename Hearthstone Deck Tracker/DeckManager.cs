﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hearthstone_Deck_Tracker.Enums;
using Hearthstone_Deck_Tracker.Hearthstone;
using Hearthstone_Deck_Tracker.Utility.Extensions;
using Hearthstone_Deck_Tracker.Utility.Logging;
using Hearthstone_Deck_Tracker.Windows;

namespace Hearthstone_Deck_Tracker
{
	public class DeckManager
	{
		private static bool _waitingForClass;
		private static bool _waitingForUserInput;
		private static int _waitingForDraws;
		public static Guid IgnoredDeckId;
		public static List<Card> NotFoundCards { get; set; } = new List<Card>(); 

		public static async Task DetectCurrentDeck()
		{
			var deck = DeckList.Instance.ActiveDeckVersion;
			if(deck == null || !Config.Instance.AutoDeckDetection || deck.DeckId == IgnoredDeckId || _waitingForClass || _waitingForUserInput)
				return;
			if(string.IsNullOrEmpty(Core.Game.Player.Class))
			{
				_waitingForClass = true;
				while(string.IsNullOrEmpty(Core.Game.Player.Class))
					await Task.Delay(100);
				_waitingForClass = false;
			}
			var cardEntites = Core.Game.Player.AllCardEntities.Where(x => x.Entity != null && (x.Entity.IsMinion || x.Entity.IsSpell || x.Entity.IsWeapon) && !x.Created && !x.Stolen).GroupBy(x => x.CardId).ToList();
			var notFound = cardEntites.Where(x => !deck.Cards.Any(c => c.Id == x.Key && c.Count >= x.Count())).ToList();
			if(notFound.Any())
			{
				NotFoundCards = notFound.SelectMany(x => x).Select(x => x.Entity?.Card).Distinct().ToList();
				await AutoSelectDeck(Core.Game.Player.Class, Core.Game.CurrentGameMode, cardEntites);
			}
			else
				NotFoundCards.Clear();
		}
		private static async Task AutoSelectDeck(string heroClass, GameMode mode, List<IGrouping<string, CardEntity>> cardEntites = null)
		{
			_waitingForDraws++;
			await Task.Delay(500);
			_waitingForDraws--;
			if(_waitingForDraws > 0)
				return;
			var validDecks = DeckList.Instance.Decks.Where(x => x.Class == heroClass && !x.Archived).Select(x => x.GetSelectedDeckVersion()).ToList();
			if(mode == GameMode.Arena)
				validDecks = validDecks.Where(x => x.IsArenaDeck && x.IsArenaRunCompleted != true).ToList();
			else if(mode != GameMode.None)
				validDecks = validDecks.Where(x => !x.IsArenaDeck).ToList();
			if(validDecks.Count > 1 && cardEntites != null)
				validDecks = validDecks.Where(x => cardEntites.All(ce => x.Cards.Any(c => c.Id == ce.Key && c.Count >= ce.Count()))).ToList();
			if(validDecks.Count == 0)
			{
				Log.Info("Could not find matching deck.");
				ShowDeckSelectionDialog(validDecks);
				return;
			}
			if(validDecks.Count == 1)
			{
				Log.Info("Found one matching deck!");
				Core.MainWindow.SelectDeck(validDecks.Single(), true);
				return;
			}
			var lastUsed = DeckList.Instance.LastDeckClass.FirstOrDefault(x => x.Class == heroClass);
			if(lastUsed != null)
			{
				var deck = validDecks.FirstOrDefault(x => x.DeckId == lastUsed.Id);
				if(deck != null)
				{
					Log.Info($"Last used {heroClass} deck matches!");
					Core.MainWindow.SelectDeck(deck, true);
					return;
				}
			}
			ShowDeckSelectionDialog(validDecks);
		}

		private static void ShowDeckSelectionDialog(List<Deck> decks)
		{
			decks.Add(new Deck("Use no deck", "", new List<Card>(), new List<string>(), "", "", DateTime.Now, false, new List<Card>(),
								   SerializableVersion.Default, new List<Deck>(), false, "", Guid.Empty, ""));
			if(decks.Count == 1 && DeckList.Instance.ActiveDeckVersion != null)
			{
				decks.Add(new Deck("No match - Keep using active deck", "", new List<Card>(), new List<string>(), "", "", DateTime.Now, false,
								   new List<Card>(), SerializableVersion.Default, new List<Deck>(), false, "", Guid.Empty, ""));
			}
			_waitingForUserInput = true;
			var dsDialog = new DeckSelectionDialog(decks);
			dsDialog.ShowDialog();

			var selectedDeck = dsDialog.SelectedDeck;
			if(selectedDeck != null)
			{
				if(selectedDeck.Name == "Use no deck")
				{
					Core.MainWindow.SelectDeck(null, true);
					NotFoundCards.Clear();
				}
				else if(selectedDeck.Name == "No match - Keep using active deck")
				{
					IgnoredDeckId = DeckList.Instance.ActiveDeckVersion?.DeckId ?? Guid.Empty;
					Log.Info($"Now ignoring {DeckList.Instance.ActiveDeckVersion?.Name}");
					NotFoundCards.Clear();
				}
				else
				{
					Log.Info("Selected deck: " + selectedDeck.Name);
					Core.MainWindow.SelectDeck(selectedDeck, true);
				}
			}
			else
			{
				Core.MainWindow.ShowMessage("Auto deck selection disabled.", "This can be re-enabled by selecting \"AUTO\" in the bottom right of the deck picker.").Forget();
				Core.MainWindow.DeckPickerList.UpdateAutoSelectToggleButton();
				Config.Save();
			}
			_waitingForUserInput = false;
		}
	}
}
