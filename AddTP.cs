/// <summary>
/// Třída přiřadí/aktualizuje technologický postup nad sestavou pro celý souhrnný kusovník.
/// 
/// Potřebné externí akce, tabulky, sloupce v Heliosu:
/// Tabulky:         není
/// Externí sloupce: není
/// Externí akce:    není
/// </summary>
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Threading.Tasks;
using ddPlugin;
using System.Runtime.InteropServices;
using IniParser;
using IniParser.Model;
using System.Reflection;
using System.Windows.Forms;

namespace plugin2JCPTechProc
{
    public class AddTP : IHePlugin2
    {
        private IHeQuery sql;
        private bool showsql = false;

        [ExportDllAttribute.ExportDll("PluginGetSysAndClassName", CallingConvention.StdCall)]
        public static UInt32 PluginGetSysAndClassName(int pointerToFirstByteOfPluginNameBuffer)
        {
            string reg = MethodBase.GetCurrentMethod().DeclaringType.Namespace + "." + System.Reflection.MethodBase.GetCurrentMethod().DeclaringType.Name;
            byte[] ansiName = Encoding.ASCII.GetBytes(reg);
            if (pointerToFirstByteOfPluginNameBuffer != IntPtr.Zero.ToInt32())
            {
                Marshal.Copy(ansiName, 0, (IntPtr)pointerToFirstByteOfPluginNameBuffer, ansiName.Length);
            }
            return Convert.ToUInt32(ansiName.Length);
        }

        /// <summary>
        /// Povinná funkce pro helios, která vrací licenční číslo.
        /// Využívá NuGet třídu ini-parser.
        /// </summary>
        public string PartnerIdentification()
        {
            string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var parser = new FileIniDataParser();
            IniData data = parser.ReadFile(Path.Combine(assemblyFolder, "Licence.INI"));
            return data["HELIOS"]["Licence"];
        }

        /// <summary>
        /// Automaticky volaná funkce z heliosu při zavolání pluginnu.
        /// </summary>
        public void Run(IHelios helios)
        {
            
        }
    }
}
