/*************************************************************************
 *
 *   file		: Quest.cs
 *   copyright		: (C) The WCell Team
 *   email		: info@wcell.org
 *   last changed	: $LastChangedDate: 2008-04-08 11:02:58 +0200 (�t, 08 IV 2008) $
 *   last author	: $LastChangedBy: domiii $
 *   revision		: $Rev: 244 $
 *
 *   This program is free software; you can redistribute it and/or modify
 *   it under the terms of the GNU General Public License as published by
 *   the Free Software Foundation; either version 2 of the License, or
 *   (at your option) any later version.
 *
 *************************************************************************/

using System;
using WCell.Constants.Achievements;
using WCell.Constants.NPCs;
using WCell.Constants.Quests;
using WCell.RealmServer.Entities;
using WCell.RealmServer.Handlers;
using WCell.RealmServer.NPCs;
using WCell.RealmServer.Database;
using WCell.Constants.Spells;
using System.Collections.Generic;
using WCell.Util.Data;

namespace WCell.RealmServer.Quests
{
	public class Quest
	{
		public byte Entry;

		private readonly QuestLog m_Log;
		private bool m_saved;

		private readonly QuestRecord m_record;

		/// <summary>
		/// Template on which is this quest based, we might actually somehow cache only used templates
		/// in case someone would requested uncached template, we'd load it from DB/XML
		/// </summary>
		public readonly QuestTemplate Template;

		/// <summary>
		/// The time at which this Quest will expire for timed quests.
		/// </summary>
		public DateTime Until;

		[NotPersistent]
		public readonly List<QuestTemplate> RequiredQuests = new List<QuestTemplate>(3);

		/// <summary>
		/// Initializes a new instance of the <see cref="Quest"/> class.
		/// Which represents one item in character's <seealso cref="QuestLog"/>
		/// </summary>
		/// <param name="template">The Quest Template.</param>
		internal Quest(QuestLog log, QuestTemplate template, int slot)
			: this(log, template, new QuestRecord(template.Id, log.Owner.EntityId.Low))
		{

			if (template.GOInteractions != null)
			{
				UsedGOs = new uint[template.GOInteractions.Length];
			}
			if (template.NPCInteractions != null)
			{
				KilledNPCs = new uint[template.NPCInteractions.Length];
			}
			if (template.SpellCastObjectives != null)
			{
				CastedSpells = new int[template.SpellCastObjectives.Length];
			}
			if (template.AreaTriggerObjectives.Length > 0)
			{
				VisitedATs = new bool[template.AreaTriggerObjectives.Length];
			}

			Slot = slot;
		}

		/// <summary>
		/// Recreate existing Quest-progress
		/// </summary>
		internal Quest(QuestLog log, QuestRecord record, QuestTemplate template)
			: this(log, template, record)
		{
			m_saved = true;

			// reset initial state

			if (template.GOInteractions != null)
			{
				if (UsedGOs == null)
				{
					UsedGOs = new uint[template.NPCInteractions.Length];
				}

				for (var i = 0; i < Template.NPCInteractions.Length; i++)
				{
					var interaction = Template.NPCInteractions[i];
					log.Owner.SetQuestCount(Slot, interaction.Index, (byte)UsedGOs[i]);
				}
			}
			if (Template.NPCInteractions != null)
			{
				if (KilledNPCs == null)
				{
					KilledNPCs = new uint[template.NPCInteractions.Length];
				}

				for (var i = 0; i < Template.NPCInteractions.Length; i++)
				{
					var interaction = Template.NPCInteractions[i];
					log.Owner.SetQuestCount(Slot, interaction.Index, (byte)KilledNPCs[i]);
				}
			}
			UpdateStatus();
		}

		private Quest(QuestLog log, QuestTemplate template, QuestRecord record)
		{
			m_record = record;

			if (template.CollectableItems.Length > 0)
			{
				CollectedItems = new int[template.CollectableItems.Length];
			}

			m_Log = log;
			Template = template;
		}

		/// <summary>
		/// Amounts of picked up Items
		/// </summary>
		public readonly int[] CollectedItems;

		/// <summary>
		/// Amounts of killed NPCs
		/// </summary>
		public uint[] KilledNPCs
		{
			get { return m_record.KilledNPCs; }
			private set { m_record.KilledNPCs = value; }
		}

		/// <summary>
		/// Amounts of interacted GameObjects
		/// </summary>
		public uint[] UsedGOs
		{
			get { return m_record.UsedGOs; }
			private set { m_record.UsedGOs = value; }
		}

		/// <summary>
		/// Spells casted
		/// </summary>
		public int[] CastedSpells
		{
			get { return m_record.CastedSpells; }
			private set { m_record.CastedSpells = value; }
		}

		/// <summary>
		/// Visited AreaTriggers
		/// </summary>
		public bool[] VisitedATs
		{
			get { return m_record.VisitedATs; }
			private set { m_record.VisitedATs = value; }
		}

		public int Slot
		{
			get { return m_record.Slot; }
			set { m_record.Slot = value; }
		}

		public uint TemplateId
		{
			get { return m_record.QuestTemplateId; }
			set { m_record.QuestTemplateId = value; }
		}

		/// <summary>
		/// Current status of quest in QuestLog
		/// 
		/// </summary>
		public QuestStatus Status
		{
			get
			{
				var chr = m_Log.Owner;
				if (Template.IsTooHighLevel(chr))
				{
					return QuestStatus.TooHighLevel;
				}

				if (CompleteStatus == QuestCompleteStatus.Completed)
				{
					return Template.Repeatable ? QuestStatus.RepeateableCompletable : QuestStatus.Completable;
				}
				return QuestStatus.NotCompleted;
			}
		}


		/// <summary>
		/// 
		/// </summary>
		/// <returns>
		/// 	<c>true</c> if [is quest completed] [the specified qt]; otherwise, <c>false</c>.
		/// </returns>
		public QuestCompleteStatus CompleteStatus
		{
			get { return m_Log.Owner.GetQuestState(Slot); }
			set { m_Log.Owner.SetQuestState(Slot, value); }
		}

		public bool IsSaved
		{
			get { return m_saved; }
		}

		public bool CheckCompletedStatus()
		{
			if (Template.CompleteHandler != null &&
				Template.CompleteHandler(this))
			{
				return true;
			}

			for (var i = 0; i < Template.CollectableItems.Length; i++)
			{
				if (CollectedItems[i] < Template.CollectableItems[i].Amount)
				{
					return false;
				}
			}

			if (Template.GOInteractions != null)
			{
				for (var i = 0; i < Template.GOInteractions.Length; i++)
				{
					if (UsedGOs[i] < Template.GOInteractions[i].Amount)
					{
						return false;
					}
				}
			}

			if (Template.NPCInteractions != null)
			{
				for (var i = 0; i < Template.NPCInteractions.Length; i++)
				{
					if (KilledNPCs[i] < Template.NPCInteractions[i].Amount)
					{
						return false;
					}
				}
			}

			if (CastedSpells != null)
			{
				for (var i = 0; i < CastedSpells.Length; i++)
				{
					//if (CastedSpells[i] < Template.SpellCastObjectives[i])
					// TODO: Allow to require more than one spellcast
					if (CastedSpells[i] < 1)
					{
						return false;
					}
				}
			}

			// TODO: Fix it
			//if ((Template.ObjTriggerIds[i] != 0) && CurrentCounts[i] == 1)
			//{
			//    return false;
			//}
			//if (!string.IsNullOrEmpty(Template.ObjTexts[i]) && CurrentCounts[i] != 1)
			//{
			//    return false;
			//}
			return true;
		}

		public void SignalSpellCasted(SpellId casted)
		{
			if (CastedSpells == null)
			{
				return;
			}

			for (var i = 0; i < Template.SpellCastObjectives.Length; i++)
			{
				var spell = Template.SpellCastObjectives[i];
				if (spell == casted)
				{
					CastedSpells[i]++;
					UpdateStatus();
					break;
				}
			}
		}

		public void SignalATVisited(uint id)
		{
			if (VisitedATs == null)
			{
				return;
			}

			for (var i = 0; i < Template.AreaTriggerObjectives.Length; i++)
			{
				var atId = Template.AreaTriggerObjectives[i];
				if (atId == id)
				{
					VisitedATs[i] = true;
					UpdateStatus();
					break;
				}
			}
		}

		public void OfferQuestReward(IQuestHolder qHolder)
		{
			QuestHandler.SendQuestGiverOfferReward(qHolder, Template, m_Log.Owner);
		}

		/// <summary>
		/// Tries to hand out the rewards, archives this quest and sends details about the next quest in the chain (if any).
		/// </summary>
		/// <param name="qHolder"></param>
		/// <param name="rewardSlot"></param>
		public bool TryFinish(IQuestHolder qHolder, uint rewardSlot)
		{
			var chr = m_Log.Owner;
			chr.OnInteract(qHolder as WorldObject);

			if (qHolder is WorldObject && !chr.IsInRadius((WorldObject)qHolder, NPCMgr.DefaultInteractionDistance))
			{
				NPCHandler.SendNPCError(chr, qHolder, VendorInventoryError.TooFarAway);
				return false;
			}

			if (Template.TryGiveRewards(m_Log.Owner, qHolder, rewardSlot))
			{
				ArchiveQuest();
				QuestHandler.SendComplete(Template, chr);

				if (Template.FollowupQuestId != 0)
				{
					var nq = QuestMgr.GetTemplate(Template.FollowupQuestId);
					if (nq != null && qHolder.QuestHolderInfo.QuestStarts.Contains(nq))
					{
						// Offer the next Quest if its also offered by the same QuestGiver
						QuestHandler.SendDetails(qHolder, nq, chr, true);
                        if (nq.Flags.HasFlag(QuestFlags.AutoAccept))
                            chr.QuestLog.TryAddQuest(nq, qHolder);
					}
				}
				chr.Achievements.CheckPossibleAchievementUpdates(AchievementCriteriaType.CompleteQuest,(uint) chr.QuestLog.FinishedQuests.Count);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Removes Quest from Active log and adds it to the finished Quests
		/// </summary>
		public void ArchiveQuest()
		{
			if (!m_Log.m_FinishedQuests.Contains(Template.Id))
			{
				m_Log.m_FinishedQuests.Add(Template.Id);
				Template.NotifyFinished(this);
				if (Template.IsDaily)
				{
					//TODO Add Daily Quest logic
					//m_CurrentDailyCount++;
				}
			}
			m_Log.RemoveQuest(this);
		}

		/// <summary>
		/// Cancels the quest and removes it from the QuestLog
		/// </summary>
		public void Cancel(bool failed)
		{
			Template.NotifyCancelled(this, failed);
			m_Log.RemoveQuest(this);
		}

		internal uint GetPacketStatus()
		{
			return CompleteStatus == QuestCompleteStatus.Completed ? 4 : (uint)Template.GetAvailability(m_Log.Owner);
		}

		public void UpdateStatus()
		{
			if (CheckCompletedStatus())
			{
				m_Log.Owner.SetQuestState(Slot, QuestCompleteStatus.Completed);
				QuestHandler.SendQuestUpdateComplete(m_Log.Owner, Template.Id);
				//if (m_Log.Owner.GetQuestState(m_Slot) != QuestStatusLog.Completed)
				//{
				//    m_Log.Owner.SetQuestState(m_Slot, QuestStatusLog.Completed);
				//    //QuestHandler.SendComplete(Template, m_Log.Owner);
				//}
			}
			else
			{
				m_Log.Owner.SetQuestState(Slot, QuestCompleteStatus.NotCompleted);
				//if (m_Log.Owner.GetQuestState(m_Slot) != QuestStatusLog.NotCompleted)
				//{
				//    m_Log.Owner.SetQuestState(m_Slot, QuestStatusLog.NotCompleted);
				//}
			}
		}

		public void Save()
		{
			if (m_saved)
			{
				m_record.Update();
			}
			else
			{
				m_record.Create();
				m_saved = true;
			}
		}

		public void Delete()
		{
			if (m_saved)
			{
				m_record.Delete();
				m_saved = false;
			}
		}

		public override string ToString()
		{
			return Template.ToString();
		}
	}
}