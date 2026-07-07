using System;
using System.Windows.Forms;

namespace DogePet;

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
            MessageBox.Show($"DogePet crashed:\n\n{ex}", "DogePet Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Console.WriteLine("FATAL ERROR: " + ex);
        }
    }
}
