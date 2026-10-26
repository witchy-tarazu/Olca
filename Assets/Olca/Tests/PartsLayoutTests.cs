using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Customization;

namespace Olca.Tests
{
    public class PartsLayoutTests
    {
        private static readonly Vector2Int[] L = { new Vector2Int(0,0), new Vector2Int(0,1), new Vector2Int(0,2), new Vector2Int(1,2) };
        private static PartsBoardDefinition Board(int width = 6, int height = 6) => new PartsBoardDefinition(width,height,
            Enumerable.Range(0,height).SelectMany(y=>Enumerable.Range(0,width).Select(x=>new Vector2Int(x,y))));
        private static readonly IReadOnlyDictionary<string,PartShape> Definitions = new Dictionary<string,PartShape> { ["l"] = new PartShape(L), ["dot"] = new PartShape(new[]{new Vector2Int(0,0)}) };
        private static readonly IReadOnlyDictionary<string,int> Owned = new Dictionary<string,int> { ["l"] = 2, ["dot"] = 2 };
        private static PartPlacement Place(string instance,string id,int x,int y,PartRotation rotation = PartRotation.None) => new PartPlacement(instance,id,new Vector2Int(x,y),rotation);
        [TestCase(PartRotation.None, "0,0;0,1;0,2;1,2")]
        [TestCase(PartRotation.Clockwise90, "0,0;1,0;2,0;0,1")]
        [TestCase(PartRotation.HalfTurn, "0,0;1,0;1,1;1,2")]
        [TestCase(PartRotation.Clockwise270, "2,0;0,1;1,1;2,1")]
        public void 回転は正規化した四方向だけで元形状を変更しない(PartRotation rotation,string expected)
        {
            var shape = new PartShape(L); string Serialize(IEnumerable<Vector2Int> v)=>string.Join(";",v.Select(p=>$"{p.X},{p.Y}"));
            Assert.AreEqual(expected,Serialize(shape.RotatedCells(rotation))); Assert.AreEqual("0,0;0,1;0,2;1,2",Serialize(shape.Cells));
            Assert.AreEqual(4,Enum.GetValues(typeof(PartRotation)).Length); Assert.Throws<ArgumentOutOfRangeException>(()=>shape.RotatedCells((PartRotation)45));
        }
        [Test]
        public void 同種を所持数まで配置でき隣接関係で配置可否を変えない()
        {
            foreach(int x in new[]{2,4}) {
                Assert.IsTrue(PartsLayout.TryCreate(Board(),Definitions,Owned,new[]{Place("a","l",0,0),Place("b","l",x,0)},out var layout,out var error));
                Assert.AreEqual(PartsLayoutError.None,error);Assert.AreEqual(8,layout.OccupiedCells.Count);Assert.AreEqual(2,layout.Placements.Count);
            }
            Assert.AreEqual(2,Owned["l"]);
        }
        [TestCase("missing",PartsLayoutError.MissingDefinition)]
        [TestCase("overlap",PartsLayoutError.Overlap)]
        [TestCase("duplicate",PartsLayoutError.DuplicateInstance)]
        [TestCase("count",PartsLayoutError.NotOwned)]
        [TestCase("outside",PartsLayoutError.OutsideBoard)]
        public void 不正な候補は部分的な配置を返さない(string kind,PartsLayoutError expected)
        {
            var placements = new List<PartPlacement>{Place("a","dot",0,0)};
            if(kind=="missing")placements.Add(Place("b","unknown",1,0));
            if(kind=="overlap")placements.Add(Place("b","l",0,0));
            if(kind=="duplicate")placements.Add(Place("a","dot",1,0));
            if(kind=="count"){placements.Add(Place("b","dot",1,0));placements.Add(Place("c","dot",2,0));}
            if(kind=="outside")placements.Add(Place("b","l",5,5));
            Assert.IsFalse(PartsLayout.TryCreate(Board(),Definitions,Owned,placements,out var layout,out var error));Assert.IsNull(layout);Assert.AreEqual(expected,error);Assert.AreEqual(2,Owned["dot"]);
        }
        [Test]
        public void 全マスの解放を要求し回転後の形状で判定する()
        {
            var board = new PartsBoardDefinition(3,2,new[]{new Vector2Int(0,0),new Vector2Int(1,0),new Vector2Int(2,0),new Vector2Int(0,1)});
            Assert.IsTrue(PartsLayout.TryCreate(board,Definitions,Owned,new[]{Place("a","l",0,0,PartRotation.Clockwise90)},out _,out _));
            Assert.IsFalse(PartsLayout.TryCreate(board,Definitions,Owned,new[]{Place("a","dot",1,1)},out _,out var error));Assert.AreEqual(PartsLayoutError.LockedCell,error);
        }
        [Test]
        public void 定義と結果は入力配列から独立し空配置や全未解放盤面を扱える()
        {
            var source = L.ToArray();var shape = new PartShape(source);source[0]=new Vector2Int(999,999);Assert.AreEqual(new Vector2Int(0,0),shape.Cells[0]);
            var unlocked = new[]{new Vector2Int(0,0)};var board=new PartsBoardDefinition(1,1,unlocked);unlocked[0]=new Vector2Int(9,9);Assert.IsTrue(board.IsUnlocked(new Vector2Int(0,0)));
            var candidates = new[]{Place("a","dot",0,0)};Assert.IsTrue(PartsLayout.TryCreate(board,Definitions,Owned,candidates,out var result,out _));candidates[0]=Place("b","dot",9,9);Assert.AreEqual("a",result.Placements[0].InstanceId);
            Assert.Throws<NotSupportedException>(()=>((IDictionary<Vector2Int,string>)result.OccupiedCells).Clear());
            Assert.IsTrue(PartsLayout.TryCreate(new PartsBoardDefinition(1,1,Array.Empty<Vector2Int>()),Definitions,Owned,Array.Empty<PartPlacement>(),out var empty,out _));Assert.IsEmpty(empty.OccupiedCells);
        }
        [Test]
        public void 入力辞書の大小文字無視比較を使って同じ所持枠を別名で重複使用しない()
        {
            var definitions = new Dictionary<string,PartShape>(StringComparer.OrdinalIgnoreCase){{"dot",Definitions["dot"]}};
            var owned = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase){{"dot",1}};
            Assert.IsFalse(PartsLayout.TryCreate(Board(),definitions,owned,new[]{Place("a","dot",0,0),Place("b","DOT",1,0)},out var layout,out var error));
            Assert.IsNull(layout); Assert.AreEqual(PartsLayoutError.MissingDefinition,error); Assert.AreEqual(1,owned["dot"]);
        }
        [Test]
        public void 不正定義と未定義角度を拒否し座標加算で例外や折返しを起こさない()
        {
            Assert.Throws<ArgumentException>(()=>new PartShape(Array.Empty<Vector2Int>()));Assert.Throws<ArgumentException>(()=>new PartShape(new[]{new Vector2Int(0,0),new Vector2Int(0,0)}));
            Assert.Throws<ArgumentException>(()=>new PartsBoardDefinition(1,1,new[]{new Vector2Int(1,0)}));Assert.Throws<ArgumentOutOfRangeException>(()=>new PartsBoardDefinition(0,1,Array.Empty<Vector2Int>()));
            Assert.Throws<ArgumentOutOfRangeException>(()=>Place("a","l",0,0,(PartRotation)(-90)));
            var board=new PartsBoardDefinition(int.MaxValue,1,new[]{new Vector2Int(int.MaxValue-1,0)});
            var wide=new Dictionary<string,PartShape>{{"wide",new PartShape(new[]{new Vector2Int(0,0),new Vector2Int(2,0)})}};
            Assert.IsFalse(PartsLayout.TryCreate(board,wide,new Dictionary<string,int>{{"wide",1}},new[]{Place("a","wide",int.MaxValue-1,0)},out var layout,out var error));Assert.IsNull(layout);Assert.AreEqual(PartsLayoutError.OutsideBoard,error);
            var extreme=new PartShape(new[]{new Vector2Int(0,0),new Vector2Int(0,int.MaxValue)});Assert.AreEqual(int.MaxValue,extreme.RotatedCells(PartRotation.Clockwise90).Max(p=>p.X));
        }
    }
}
