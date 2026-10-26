using System;
using System.Collections.Generic;
using System.Linq;
using Olca.Core.Logic.Customization;

namespace Olca.Core.Domain
{
    public sealed partial class ClearReturnSession
    {
        public CustomizationProgress CustomizationSnapshot => (current?.Customization ?? new CustomizationProgress()).Copy();
        private static CustomizationProgress BindCustomization(ClearReturnProfile profile, CustomizationCatalog catalog)
        {
            if (catalog == null) throw new ArgumentException("装備定義が必要です");
            var state = (profile.Customization ?? new CustomizationProgress()).Copy();
            if (state.BoardId == null) {
                state.BoardId = catalog.BoardId;
                state.UnlockedCells = catalog.Board.UnlockedCells.Select(v => new CustomizationCell { X = v.X, Y = v.Y }).ToArray();
            }
            if (state.BoardId != catalog.BoardId || state.UnlockedCells.Any(v => v.X >= catalog.Board.Width || v.Y >= catalog.Board.Height)) throw new ArgumentException("保存済み盤面と登録定義が一致しません");
            profile.Customization = state; return state;
        }
        /// <summary>入手元が確定した報酬を一度だけ保存する。装飾品/パーツの道中取得は禁止。</summary>
        public bool TryGrantCustomization(CustomizationCatalog catalog, CustomizationGrant grant)=>TryGrantCustomizationCore(catalog,grant,false);
        private bool TryGrantCustomizationCore(CustomizationCatalog catalog, CustomizationGrant grant,bool fromSavedCandidate)
        {
            if (!Available()) return Fail("未保存の処理を先に確定してください");
            try {
                if (grant == null || catalog == null) throw new ArgumentException("装備の取得定義が必要です");
                if(!fromSavedCandidate&&CustomizationSnapshot.Acquisitions.Any(v=>v.Candidates.Any(c=>c.RequestId==grant.RequestId)))return Fail("保存された候補の受取経路を使ってください");
                var prior = CustomizationSnapshot.Receipts.SingleOrDefault(v => v.RequestId == grant.RequestId); string hash = grant.Fingerprint(catalog.BoardId);
                if (prior != null) { if (prior.Fingerprint != hash) return Fail("同じ取得要求の内容が一致しません"); Error = null; return true; }
                bool clearReward = grant.ClearRunId != null;
                if (clearReward && (current.Flow?.RunId != grant.ClearRunId || current.Flow.Outcome != RunReturnOutcome.Cleared || current.NextDeparture?.ParentRunId == grant.ClearRunId)) return Fail("クリアが確定した攻略の報酬が必要です");
                if (!clearReward && (grant.Accessories.Count != 0 || grant.Parts.Count != 0) && (customizationRunActive?.Invoke()==true || !ExchangeAvailable())) return Fail("装飾品とパーツは道中で取得できません");
                var next = current.Copy(); var state = BindCustomization(next, catalog);
                if (grant.Accessories.Any(id => !catalog.Accessories.Contains(id) || state.OwnedAccessoryIds.Contains(id)) || grant.Parts.Keys.Any(id => !catalog.Parts.ContainsKey(id)) || grant.UnlockItems.Keys.Any(id => !catalog.UnlockItems.ContainsKey(id))) return Fail("未登録または取得済みの装備です");
                state.OwnedAccessoryIds = state.OwnedAccessoryIds.Concat(grant.Accessories).ToArray();
                CustomizationItemCount[] Add(CustomizationItemCount[] source, IReadOnlyDictionary<string, int> additions) {
                    var counts = source.ToDictionary(v => v.Id, v => v.Count);
                    foreach (var pair in additions) counts[pair.Key] = checked((counts.TryGetValue(pair.Key, out int count) ? count : 0) + pair.Value);
                    return counts.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => new CustomizationItemCount { Id = v.Key, Count = v.Value }).ToArray();
                }
                state.OwnedParts = Add(state.OwnedParts, grant.Parts); state.OwnedUnlockItems = Add(state.OwnedUnlockItems, grant.UnlockItems);
                state.Receipts = state.Receipts.Concat(new[] { new CustomizationGrantReceipt { RequestId = grant.RequestId, Fingerprint = hash } }).ToArray();
                return Stage(next);
            } catch (Exception e) when (e is ArgumentException || e is OverflowException) { return Fail(e.Message); }
        }
        /// <summary>登録アイテムの固定絶対座標だけを解放する。所有記録と適用済みを分け、消費/価格は推測しない。</summary>
        public bool TryApplyCustomizationUnlock(CustomizationCatalog catalog, string itemId)
        {
            if (!Available()) return Fail("未保存の処理を先に確定してください");
            try {
                var next = current.Copy(); var state = BindCustomization(next, catalog);
                if (string.IsNullOrWhiteSpace(itemId) || !state.OwnedUnlockItems.Any(v => v.Id == itemId) || !catalog.UnlockItems.TryGetValue(itemId, out var cells)) return Fail("所持するマス解放アイテムが必要です");
                if (state.AppliedUnlockItemIds.Contains(itemId)) { Error = null; return true; }
                var unlocked = state.UnlockedCells.Select(v => new Vector2Int(v.X, v.Y)).Concat(cells).Distinct().OrderBy(v => v.Y).ThenBy(v => v.X);
                state.UnlockedCells = unlocked.Select(v => new CustomizationCell { X = v.X, Y = v.Y }).ToArray();
                state.AppliedUnlockItemIds = state.AppliedUnlockItemIds.Concat(new[] { itemId }).ToArray(); return Stage(next);
            } catch (ArgumentException e) { return Fail(e.Message); }
        }
        /// <summary>登録定義で保存配置を再検証する。未登録/不一致でも保存内容を削除しない。</summary>
        public bool TryGetCustomizationLayout(CustomizationCatalog catalog, out PartsLayout layout, out string error)
        {
            layout = null; error = null;
            if (!Ready) { error = "進行保存が未読込です"; return false; }
            try {
                var state = BindCustomization(current.Copy(), catalog);
                if (state.EquippedAccessoryIds.Length > catalog.AccessorySlots || state.EquippedAccessoryIds.Any(id => !catalog.Accessories.Contains(id))) { error = "装飾品定義が保存と一致しません"; return false; }
                var board = new PartsBoardDefinition(catalog.Board.Width, catalog.Board.Height, state.UnlockedCells.Select(v => new Vector2Int(v.X, v.Y)));
                bool valid = PartsLayout.TryCreate(board, catalog.Parts, state.OwnedParts.ToDictionary(v => v.Id, v => v.Count), state.Placements.Select(v => v.ToPlacement()), out layout, out var reason);
                if (!valid) error = "保存パーツと登録定義が一致しません: " + reason; return valid;
            } catch (ArgumentException e) { error = e.Message; return false; }
        }
        public bool TrySetCustomizationLoadout(CustomizationCatalog catalog, IEnumerable<string> accessories, IEnumerable<PartPlacement> placements)
        {
            if (customizationRunActive?.Invoke()==true || !ExchangeAvailable()) return Fail("装備と配置の変更は出撃前だけ可能です");
            try {
                var next = current.Copy(); var state = BindCustomization(next, catalog);
                var equipped = (accessories ?? Array.Empty<string>()).ToArray(); var parts = (placements ?? Array.Empty<PartPlacement>()).ToArray();
                if (equipped.Length > catalog.AccessorySlots || equipped.Distinct().Count() != equipped.Length || equipped.Any(v => v == null || !catalog.Accessories.Contains(v) || !state.OwnedAccessoryIds.Contains(v))) return Fail("装飾品の所持または枠が不正です");
                var board = new PartsBoardDefinition(catalog.Board.Width, catalog.Board.Height, state.UnlockedCells.Select(v => new Vector2Int(v.X, v.Y)));
                if (!PartsLayout.TryCreate(board, catalog.Parts, state.OwnedParts.ToDictionary(v => v.Id, v => v.Count), parts, out _, out var reason)) return Fail("パーツ配置が不正です: " + reason);
                state.EquippedAccessoryIds = equipped; state.Placements = parts.Select(v => new SavedPartPlacement { InstanceId = v.InstanceId, PartId = v.PartId, X = v.Anchor.X, Y = v.Anchor.Y, Rotation = v.Rotation }).ToArray();
                return Stage(next);
            } catch (ArgumentException e) { return Fail(e.Message); }
        }
    }
}
