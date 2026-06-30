// Because <UseWindowsForms> adds an implicit global `using System.Windows.Forms;`,
// several type names collide with their WPF counterparts. These alias directives make
// the bare names resolve to the WPF / Win32 types we actually use everywhere.
global using Application = System.Windows.Application;
global using Clipboard = System.Windows.Clipboard;
global using MessageBox = System.Windows.MessageBox;
global using Button = System.Windows.Controls.Button;
global using UserControl = System.Windows.Controls.UserControl;
global using Brush = System.Windows.Media.Brush;
global using Brushes = System.Windows.Media.Brushes;
global using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
