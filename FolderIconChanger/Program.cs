using System;
using System.IO;
using System.Windows.Forms;

namespace FolderIconChanger;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // 우클릭 메뉴로 실행된 경우: 첫 인자가 폴더 경로
        string initialFolder = "";
        if (args.Length > 0 && Directory.Exists(args[0]))
            initialFolder = Path.GetFullPath(args[0].Trim('"'));

        Application.Run(new MainForm(initialFolder));
    }
}
