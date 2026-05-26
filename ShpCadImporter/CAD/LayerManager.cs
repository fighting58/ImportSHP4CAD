using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Colors;

namespace ShpCadImporter.CAD
{
    /// <summary>
    /// AutoCAD 레이어 생성 및 캐싱 관리.
    /// SHP_BOUNDARY, MNUM_DATA, NTFDATE_DATA, ALIAS_DATA 레이어를 관리한다.
    /// </summary>
    public class LayerManager
    {
        /// <summary>레이어 이름 상수</summary>
        public const string LAYER_BOUNDARY = "SHP_BOUNDARY";
        public const string LAYER_MNUM = "MNUM_DATA";
        public const string LAYER_NTFDATE = "NTFDATE_DATA";
        public const string LAYER_ALIAS = "ALIAS_DATA";

        /// <summary>레이어 ObjectId 캐시</summary>
        private readonly Dictionary<string, ObjectId> _layerCache
            = new Dictionary<string, ObjectId>();

        /// <summary>
        /// 필요한 경계선 레이어 및 선택된 필드의 레이어(필드명_DATA)를 생성한다.
        /// </summary>
        public void EnsureLayers(Database db, Transaction tr, List<string> selectedFields)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            // 경계선 레이어 생성 (White)
            EnsureLayer(lt, tr, LAYER_BOUNDARY, Color.FromColorIndex(ColorMethod.ByAci, 7));

            if (selectedFields != null)
            {
                // 표준 색상 코드 배열 (1: 빨강, 2: 노랑, 3: 초록, 4: 하늘색, 5: 파랑, 6: 선홍색)
                // 이외의 다양한 필드가 체크되더라도 색상이 이쁘게 순환하도록 적용
                short[] colors = new short[] { 1, 2, 3, 4, 5, 6 };

                for (int i = 0; i < selectedFields.Count; i++)
                {
                    string fieldName = selectedFields[i];
                    string layerName = fieldName.ToUpper() + "_DATA";
                    Color col = Color.FromColorIndex(ColorMethod.ByAci, colors[i % colors.Length]);
                    EnsureLayer(lt, tr, layerName, col);
                }
            }
        }

        /// <summary>
        /// 캐싱된 레이어 이름을 반환한다.
        /// ObjectId 대신 레이어 이름 문자열로 Entity에 직접 할당한다.
        /// </summary>
        public bool HasLayer(string layerName)
        {
            return _layerCache.ContainsKey(layerName);
        }

        /// <summary>
        /// 단일 레이어를 생성하고 캐시에 등록한다.
        /// </summary>
        private void EnsureLayer(LayerTable lt, Transaction tr, 
            string layerName, Color color)
        {
            if (lt.Has(layerName))
            {
                // 이미 존재하면 ObjectId만 캐시
                _layerCache[layerName] = lt[layerName];
                return;
            }

            // 새 레이어 생성
            lt.UpgradeOpen();
            LayerTableRecord ltr = new LayerTableRecord();
            ltr.Name = layerName;
            ltr.Color = color;
            ltr.IsOff = false;
            ltr.IsFrozen = false;

            ObjectId layerId = lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
            lt.DowngradeOpen();

            _layerCache[layerName] = layerId;
        }
    }
}
