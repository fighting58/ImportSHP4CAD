using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using NetTopologySuite.IO;
using ShpCadImporter.SHP;
using ShpCadImporter.Geometry;
using ShpCadImporter.Labeling;
using ShpCadImporter.CAD;

namespace ShpCadImporter.Commands
{
    public class ImportShpCommand
    {
        [CommandMethod("IMPORT_SHP")]
        [CommandMethod("ISH")]
        public void ImportShp()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // 1. 파일 선택 (Windows Forms OpenFileDialog)
            string shpPath = SelectShpFile();
            if (string.IsNullOrEmpty(shpPath))
            {
                ed.WriteMessage("\n[취소됨] SHP 파일을 선택하지 않았습니다.\n");
                return;
            }

            // 2. SHP 속성 필드 파싱 및 선택 대화상자 호출
            List<string> availableFields = GetShpFields(shpPath, ed);
            if (availableFields.Count == 0)
            {
                ed.WriteMessage("\n[오류] SHP 파일의 DBF 헤더 정보를 읽을 수 없습니다.\n");
                return;
            }

            List<string> selectedFields;
            using (var form = new ShpCadImporter.UI.FieldSelectionForm(availableFields))
            {
                // AutoCAD 안전 모달 다이얼로그 호출
                var result = Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form);
                if (result != DialogResult.OK || form.SelectedFields.Count == 0)
                {
                    ed.WriteMessage("\n[취소됨] 가져올 필드가 선택되지 않았습니다.\n");
                    return;
                }
                selectedFields = form.SelectedFields;
            }

            try
            {
                ed.WriteMessage(string.Format("\n[진행] 파일 읽기 시작: {0}", shpPath));
                
                // 3. SHP + DBF 동적 읽기
                List<ShpFeature> features = ShpReader.Read(shpPath);
                ed.WriteMessage(string.Format("\n[진행] {0}개의 Feature를 읽었습니다.", features.Count));

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // 4. 레이어 동적 생성 (선택한 필드명_DATA 레이어 매핑)
                    LayerManager layerManager = new LayerManager();
                    layerManager.EnsureLayers(db, tr, selectedFields);

                    int totalPolygons = 0;
                    int totalLabels = 0;

                    // 5. 피처 순회 및 엔티티 생성 파이프라인
                    foreach (var feature in features)
                    {
                        // 5.1 Geometry 분해 (MultiPolygon -> 개별 PolygonRings)
                        List<PolygonRings> explodedPolygons = GeometryProcessor.Explode(feature.Geometry);

                        foreach (var rings in explodedPolygons)
                        {
                            // 5.2 중심점 계산 (Polylabel)
                            double[] labelPos = LabelEngine.GetLabelPosition(rings);

                            // 5.3 경계선 생성 (Exterior Ring)
                            CadEntityBuilder.CreatePolyline(rings.ExteriorRing, LayerManager.LAYER_BOUNDARY, btr, tr);
                            totalPolygons++;

                            // 5.3 경계선 생성 (Interior Rings - 구멍)
                            if (rings.InteriorRings != null)
                            {
                                foreach (var hole in rings.InteriorRings)
                                {
                                    CadEntityBuilder.CreatePolyline(hole, LayerManager.LAYER_BOUNDARY, btr, tr);
                                    totalPolygons++;
                                }
                            }

                            // 5.4 선택된 속성 텍스트 동적 생성 (수직 대칭 옵셋 정렬 배치)
                            if (labelPos != null)
                            {
                                double spacing = 0.8; // 줄간격 옵셋 (높이 0.5 대비 0.8 단위)
                                int N = selectedFields.Count;

                                for (int i = 0; i < N; i++)
                                {
                                    string fieldName = selectedFields[i];
                                    string v;
                                    string val = feature.Attributes.TryGetValue(fieldName, out v) ? v : string.Empty;

                                    // 수직 대칭 오프셋 공식: ( (N - 1) / 2.0 - i ) * spacing
                                    double yOffset = ((N - 1) / 2.0 - i) * spacing;
                                    double[] textPos = new double[] { labelPos[0], labelPos[1] + yOffset };

                                    // 레이어 이름 규격: 필드명 + "_DATA" (예: MNUM_DATA)
                                    string layerName = fieldName.ToUpper() + "_DATA";

                                    CadEntityBuilder.CreateText(val, textPos, layerName, btr, tr);
                                }
                                
                                totalLabels++;
                            }
                        }
                    }

                    tr.Commit();
                    ed.WriteMessage(string.Format("\n[완료] 총 {0}개의 경계선 폴리곤과 {1}개의 위치에 속성 텍스트가 생성되었습니다.\n", totalPolygons, totalLabels));
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage(string.Format("\n[오류] Import 중 오류 발생: {0}\n{1}\n", ex.Message, ex.StackTrace));
            }
        }

        private string SelectShpFile()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = "SHP 파일 선택";
                ofd.Filter = "Shapefiles (*.shp)|*.shp";
                ofd.CheckFileExists = true;

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    return ofd.FileName;
                }
            }
            return null;
        }

        private List<string> GetShpFields(string shpPath, Editor ed)
        {
            List<string> fields = new List<string>();
            try
            {
                using (var reader = new ShapefileDataReader(shpPath, NetTopologySuite.Geometries.GeometryFactory.Default))
                {
                    DbaseFileHeader header = reader.DbaseHeader;
                    for (int i = 0; i < header.NumFields; i++)
                    {
                        fields.Add(header.Fields[i].Name);
                    }
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage(string.Format("\n[디버그] GetShpFields 예외 상세: {0}\n", ex.ToString()));
            }
            return fields;
        }
    }
}
