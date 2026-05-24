using System;
using System.Windows.Forms;

namespace DodgePet;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        try
        {
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"DodgePet crashed:\n\n{ex}", "DodgePet Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Console.WriteLine("FATAL ERROR: " + ex);
        }
    }
}
