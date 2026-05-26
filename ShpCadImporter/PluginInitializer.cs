using System;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;

namespace ShpCadImporter
{
    /// <summary>
    /// AutoCAD Plugin 초기화 클래스.
    /// DLL 로드 시 자동으로 Initialize()가 호출된다.
    /// 외부 DLL(NetTopologySuite 등) 탐색을 위한 AssemblyResolve 핸들러를 등록한다.
    /// </summary>
    public class PluginInitializer : IExtensionApplication
    {
        public void Initialize()
        {
            // 외부 DLL 탐색 핸들러 등록
            // AutoCAD x64는 Plugin 폴더의 DLL을 자동 탐색하지 않으므로
            // 이 핸들러가 없으면 FileNotFoundException 발생
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

            // .NET Core / .NET 8 환경용 코드페이지 인코딩 프로바이더 전역 등록
            SHP.ShpReader.TryRegisterCodePagesProvider();

            // 초기화 완료 메시지
            Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager
                .MdiActiveDocument.Editor.WriteMessage(
                    "\n[ShpCadImporter] Plugin loaded. Type IMPORT_SHP to start.\n");
        }

        public void Terminate()
        {
            // 정리 작업 (필요 시 구현)
        }

        /// <summary>
        /// Assembly 탐색 실패 시 Plugin DLL과 동일한 폴더에서 찾는다.
        /// </summary>
        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            string folderPath = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location);

            string assemblyName = new AssemblyName(args.Name).Name;
            
            // 1. 루트 폴더에서 먼저 검색
            string assemblyPath = Path.Combine(folderPath, assemblyName + ".dll");
            if (File.Exists(assemblyPath))
            {
                return Assembly.LoadFrom(assemblyPath);
            }

            // 2. "libs" 하위 폴더에서 2차 검색 (배포 정리용)
            string libsFolderPath = Path.Combine(folderPath, "libs");
            string libsAssemblyPath = Path.Combine(libsFolderPath, assemblyName + ".dll");
            if (File.Exists(libsAssemblyPath))
            {
                return Assembly.LoadFrom(libsAssemblyPath);
            }

            return null;
        }
    }
}
