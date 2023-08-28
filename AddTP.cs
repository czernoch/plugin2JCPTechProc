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
using System.Data;
using Newtonsoft.Json;
using System.Security.Cryptography.X509Certificates;
using Microsoft.SqlServer.Server;
using System.Net.Sockets;
using static System.Net.WebRequestMethods;
using System.Runtime.InteropServices.ComTypes;

namespace plugin2JCPTechProc
{
    public class AddTP : IHePlugin2
    {
        private IHeQuery sql;
        private bool showsql = false;
        string s = string.Empty;

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
            // aktualni id
            int idkmen = helios.CurrentRecordID();
            if (idkmen <= 0)
            {
                MessageBox.Show("Není vybrán žádný záznam.");
                return;
            }

            // konec kdyz nejsem ve spravnem prehledu
            if (helios.BrowseID() != 11001)
            {
                MessageBox.Show("Spuštěno v nesprávném přehledu.");
                return;
            }

            // zmena kurzoru ve windows na cekaci
            Cursor.Current = Cursors.WaitCursor;

            // vytahnu si zakladni udaje o zpracovavane sestave
            sql = helios.OpenSQL("select SkupZbo, RegCis from TabKmenZbozi where ID = " + idkmen + ";");
            string registracnicislo = sql.FieldByNameValues("SkupZbo").ToString() + "-" + sql.FieldByNameValues("RegCis");

            // vyctu strukturní kusovník z heliosu
            sql = helios.OpenSQL("DECLARE " +
                "@datumTPV datetime," +
                "@Err int," +
                "@pracoviste int," +
                "@tarif int," +
                "@pripravnycasstrojni int," +
                "@pripravnycasobsluhy int," +
                "@operace nchar(4)," +
                "@nazev nvarchar(100);" +

                "select @datumTPV = format(getdate(), 'yyyyMMdd');\n" +
                "DECLARE @tabKusovnik_ProSouhKus TABLE (vyssi integer NULL, IDKmenZbozi integer NOT NULL, IDKVazby integer NULL, mnozstvi numeric(20,6) NOT NULL, prirez numeric(20,6) NULL, prime bit NOT NULL, RezijniMat tinyint NOT NULL, VyraditZKalkulace tinyint NOT NULL);\n" +
                "INSERT INTO @tabKusovnik_ProSouhKus EXEC @Err=hp_generujQuickKusovnik " + idkmen + ", 1, @DatumTPV, 1, 0, 1, 0, 0, 0, 0, NULL, 0, 1;" +

                "declare @data TABLE (idkmen int, opocet int, omax numeric(10,3), hmotnost numeric(10,3), " +
                "hraneni tinyint, obrabeni tinyint, krouzeni tinyint, vrtani tinyint, ukosovani tinyint, indexs char(1), operace tinyint, regcis varchar(30));\n" +

                "insert into @data select " +
                "TabKmenZbozi.ID, " +
                "isnull(TabKmenZbozi_EXT._ohyb_pocet,0), " +
                "isnull(TabKmenZbozi_EXT._ohyb_max,0), " +
                "isnull(TabKmenZbozi.hmotnost,0), " +
                "(case when TabKmenZbozi_EXT._operacenp LIKE '%H%' then 1 else 0 end) as hraneni, " +
                "(case when TabKmenZbozi_EXT._operacenp LIKE '%R%' then 1 else 0 end) as obrabeni, " +
                "(case when TabKmenZbozi_EXT._operacenp LIKE '%K%' then 1 else 0 end) as krouzeni, " +
                "(case when TabKmenZbozi_EXT._operacenp LIKE '%W%' then 1 else 0 end) as vrtani, " +
                "(case when TabKmenZbozi_EXT._operacenp LIKE '%U%' then 1 else 0 end) as ukosovani, " +
                "isnull(TabKmenZbozi_EXT._indexs, '(none)'), " +
                //"isnull(TabKmenZbozi_EXT._operacenp, '(none)'), " +
                "(case when LEN(replace(TabKmenZbozi_EXT._operacenp,' ', '')) > 0  then 1 else 0 end) as operace, " +
                "TabKmenZbozi.RegCis " +
                "from TabKmenZbozi inner join TabKmenZbozi_EXT on TabKmenZbozi_EXT.ID = TabKmenZbozi.ID where TabKmenZbozi.ID in (select IDKmenZbozi from @tabKusovnik_ProSouhKus group by IDKmenZbozi) and TabKmenZbozi.dilec = 1;\n" +

                "select * from @data;");

            List<Kusovnik> kusovnik = new List<Kusovnik>();
            string indexs;

            while (!sql.EOF())
            {
                // hazi mi to ze neni odkaz na instanci, spatne to zpracovalo prazdny zaznam operace, tak jsem dal pri NULL hodnotu (none) a zpracuju to podminkou
                // pro jistotu jsem to same udelal s indexs
                // a stejne to nepomohlo
                //if (sql.FieldByNameValues("operace") == "(none)") oper = ""; else oper = sql.FieldByNameValues("operace");
                if (sql.FieldByNameValues("indexs") == "(none)") indexs = ""; else indexs = sql.FieldByNameValues("indexs");

                kusovnik.Add(new Kusovnik()
                {
                    regcis = sql.FieldByNameValues("RegCis"),
                    idkmen = sql.FieldByNameValues("idkmen"),
                    opocet = sql.FieldByNameValues("opocet"),
                    omax = sql.FieldByNameValues("omax"),
                    hmotnost = sql.FieldByNameValues("hmotnost"),
                    hraneni = (sql.FieldByNameValues("hraneni") == 1 ? true : false),
                    obrabeni = (sql.FieldByNameValues("obrabeni") == 1 ? true : false),
                    krouzeni = (sql.FieldByNameValues("krouzeni") == 1 ? true : false),
                    vrtani = (sql.FieldByNameValues("vrtani") == 1 ? true : false),
                    ukosovani = (sql.FieldByNameValues("ukosovani") == 1 ? true : false),
                    indexs = indexs,
                    operace = (sql.FieldByNameValues("operace") == 1 ? true : false),
                });

                sql.Next();
            }

            // zjistim vsechny soucasne operace abych je mohl pripadne mazat z prechoziho nastaveni
            string idkmenin = string.Join(",", kusovnik.Select(x => x.idkmen).ToArray());
            List<Postup> postupy = new List<Postup>();

            // konec kdyz nemam zadna data, ale to se stat nemuze
            if (idkmenin.Length == 0)
            {
                MessageBox.Show("Nenalezen žádný kusovník.");
                return;
            }

            // select na technologický postup
            sql = helios.OpenSQL("select TabPostup.ID as idpostup, TabPostup.dilec as idkmen, " +
                "TabPostup.typ, " +
                "isnull(TabPostup.Operace,'') as Operace, " +
                "isnull(TabPostup.nazev, '') as nazev, " +
                "isnull(TabPostup.pracoviste, 0) as idcpraco, " +
                "isnull(TabPostup.tarif,0) as idtarifh, " +
                "CONVERT(tinyint, isnull(TabCZmeny.Platnost, 0)) as Platnost, " +
                "concat(TabCZmeny.Rada, ' - ', TabCZmeny.ciszmeny, ' :: Název: ', TabCZmeny.navrh, ', Autor: ', TabCZmeny.Autor) as zmenapopis, " +
                "TabPostup.TBC, TabPostup.TBC_T, TabPostup.TBC_Obsluhy, TabPostup.TBC_Obsluhy_T, TabPostup.TAC_J, TabPostup.TAC_J_T, " +
                "TabPostup.TAC_Obsluhy_J, TabPostup.TAC_Obsluhy_J_T, TabPostup.TAC, TabPostup.TAC_T, " +
                "TabPostup.TAC_Obsluhy, TabPostup.TAC_Obsluhy_T, TabPostup.PocetLidi, TabPostup.PocetKusu, TabPostup.PocetStroju, " +
                "isnull(TabKmenZbozi.RegCis,'') as RegCis, isnull(TabKmenZbozi.SkupZbo,'') as SkupZbo \n" +
                "FROM TabPostup INNER JOIN TabCZmeny ON TabCZmeny.ID = TabPostup.ZmenaOD left join TabKmenZbozi on TabKmenZbozi.ID = TabPostup.dilec \n" +
                "where TabPostup.ZmenaDo is NULL and TabPostup.dilec in (" + idkmenin + ")");

            while (!sql.EOF())
            { 
                postupy.Add(new Postup()
                {
                    idpostup = sql.FieldByNameValues("idpostup"),
                    idkmen = sql.FieldByNameValues("idkmen"),
                    typ = sql.FieldByNameValues("typ"),
                    operace = sql.FieldByNameValues("Operace"),
                    nazev = sql.FieldByNameValues("nazev"),
                    idcpraco = sql.FieldByNameValues("idcpraco"),
                    idtarifh = sql.FieldByNameValues("idtarifh"),
                    platnost = (sql.FieldByNameValues("Platnost") == 1 ? true : false),
                    zmenapopis = sql.FieldByNameValues("zmenapopis"),
                    regcis = sql.FieldByNameValues("RegCis"),
                    skupzbo = sql.FieldByNameValues("SkupZbo"),
                    TBC = sql.FieldByNameValues("TBC"),
                    TBC_T = sql.FieldByNameValues("TBC_T"),
                    TBC_Obsluhy = sql.FieldByNameValues("TBC_Obsluhy"),
                    TBC_Obsluhy_T = sql.FieldByNameValues("TBC_Obsluhy_T"),
                    TAC_J = sql.FieldByNameValues("TAC_J"),
                    TAC_J_T = sql.FieldByNameValues("TAC_J_T"),
                    TAC_Obsluhy_J = sql.FieldByNameValues("TAC_Obsluhy_J"),
                    TAC_Obsluhy_J_T = sql.FieldByNameValues("TAC_Obsluhy_J_T"),
                    TAC = sql.FieldByNameValues("TAC"),
                    TAC_T = sql.FieldByNameValues("TAC_T"),
                    TAC_Obsluhy = sql.FieldByNameValues("TAC_Obsluhy"),
                    TAC_Obsluhy_T = sql.FieldByNameValues("TAC_Obsluhy_T"), 
                });

                sql.Next();
            }

            // rozpracuju zmenu vzdy a kdyz bude pravda, zase ji smazu jinak zpoplatnim
            // mam na to proceduru, zavolam ji
            sql = helios.OpenSQL("declare @idzmena int; EXEC @idzmena = dbo.hpx_2JCP_zalozitZmenoveRizeni @nazev = 'Autovložení TP " + registracnicislo + "'; select @idzmena;");
            int idzmena = sql.FieldValues(0);

            // nactu konfiguraci operaci, ktera se lisi pro racice a trebic
            // read json file over newtonsoft.json
            string json = System.IO.File.ReadAllText(System.IO.Path.GetDirectoryName(Application.ExecutablePath) + "\\plugin2JCPTechProc.json");
            dynamic config = JsonConvert.DeserializeObject(json);
            string firma = "racice";
            //config[firma][0]["paleni"][0]["nazev"].ToString(); // laserove paleni

            Postup match;
            List<KusovnikRozpracovano> kusovnikRozpracovano = new List<KusovnikRozpracovano>();

            // budu tocit dilce a navazovat technologicky postup
            foreach (Kusovnik item in kusovnik)
            {
                //MessageBox.Show(item.regcis);
                match = postupy.Where(x => x.idkmen == item.idkmen && !x.platnost).FirstOrDefault(); // zjistim jestli tam je postup
                if (match.idpostup > 0)
                {
                    // obsahuje to rozpracovanou zmenu, odsypu vedle a na konci dam vedet
                    kusovnikRozpracovano.Add(new KusovnikRozpracovano() { regcis = item.regcis, zmenapopis = match.zmenapopis });
                    continue;
                }

                // výpalky
                // ---------------------------------------------------
                match = postupy.Where(x => x.idkmen == item.idkmen && x.nazev.Contains(config[firma][0]["paleni"][0]["nazev"].ToString())).FirstOrDefault(); // zjistim jestli tam je postup
                if (match == null) match = new Postup() { }; // pokud ne, pro dalsi praci jej potrebuju definovat aspon prazdny
                
                if (item.indexs == "V" && match.idpostup == 0)
                {
                    // vypalek neni v TP, tak ho tam pridam
                    s="insert into TabPostup (dilec, typ, Operace, nazev, pracoviste, tarif, ZmenaOD) values (" + 
                        item.idkmen + ", 1, '"+ 
                        config[firma][0]["paleni"][0]["operace"] + "', '" + 
                        config[firma][0]["paleni"][0]["nazev"] + "', "+ 
                        config[firma][0]["paleni"][0]["pracoviste"] +", " + 
                        config[firma][0]["paleni"][0]["tarif"] + ", " + idzmena + ")";
                    //MessageBox.Show(s);
                    helios.ExecSQL(s);
                }
                // kdyz to neni indexs V, tak zkontrolovat jestli tam neni paleni a kdyztak to smazat
                else if (item.indexs != "V" && match.idpostup > 0)
                {
                    helios.ExecSQL("update TabPostup set ZmenaDo = " + idzmena + " where ID = " + match.idpostup.ToString());
                }

                // hraneni
                // ---------------------------------------------------
                match = postupy.Where(x => x.idkmen == item.idkmen && x.nazev.Contains(config[firma][0]["hraneni"][0]["nazev"].ToString())).FirstOrDefault(); // zjistim jestli tam je postup
                if (match == null) match = new Postup() { }; // pokud ne, pro dalsi praci jej potrebuju definovat aspon prazdny

                if (item.hraneni)
                {
                    // zjistim casy pro planovani hraneni
                    double casstrojni = 0;
                    if (item.hmotnost <= 5 && match.omax <= 500) casstrojni = 0.7 * match.opocet;
                    else if (item.hmotnost <= 15 && match.omax <= 3000) casstrojni = 1.25 * match.opocet;
                    else if (item.hmotnost <= 40 && match.omax <= 4000) casstrojni = 2.5 * match.opocet;
                    else casstrojni = 5 * match.opocet;

                    double casobsluhy = 0;
                    if (item.hmotnost <= 5 && match.omax <= 500) casobsluhy = 1.4 * match.opocet;
                    else if (item.hmotnost <= 15 && match.omax <= 3000) casobsluhy = 2.5 * match.opocet;
                    else if (item.hmotnost <= 40 && match.omax <= 4000) casobsluhy = 5 * match.opocet;
                    else casstrojni = 10 * match.opocet;

                    // jestli tam neni stejna hodnota a uz to je zapsany, tak to smazu
                    if ((match.TAC_J != casstrojni || match.TAC_Obsluhy_J != casobsluhy) && match.idpostup > 0)
                    {
                        helios.ExecSQL("update TabPostup set ZmenaDo = " + idzmena + " where ID = " + match.idpostup.ToString());
                    }

                    // jestli tam neni zadna hodnota nebo je jina, tak to tam pridam
                    if ((match.TAC_J != casstrojni || match.TAC_Obsluhy_J != casobsluhy) || match.idpostup == 0)
                    {
                        s = "insert into TabPostup(\n" +
                            "dilec, typ, ZmenaOd, ZmenaDo, DavkaTPV, " +
                            "Operace, " +
                            "nazev, " +
                            "pracoviste, " +
                            "tarif, " +
                            "TBC, TBC_T, " +
                            "TBC_Obsluhy, TBC_Obsluhy_T," +
                            "TAC_J, TAC_J_T, " +
                            "TAC_Obsluhy_J, TAC_Obsluhy_J_T, " +
                            "TAC, TAC_T, " +
                            "TAC_Obsluhy, TAC_Obsluhy_T," +
                            "PocetLidi, PocetKusu, PocetStroju," +
                            "KVO, VyraditZKontrolyPosloupOper, Konf_ZahrnoutDoKapacPlan, DatPorizeni, Autor, IDZakazModif, IDVarianta" +
                            ") values(" +
                            item.idkmen + ", 1, " + idzmena + ", NULL, 1, '" +
                            config[firma][0]["hraneni"][0]["operace"] + "', '" +
                            config[firma][0]["hraneni"][0]["nazev"] + "', " +
                            config[firma][0]["hraneni"][0]["pracoviste"] + ", " +
                            config[firma][0]["hraneni"][0]["tarif"] + ", " +
                            config[firma][0]["hraneni"][0]["pripravnycasstrojni"] + ", 1, " +
                            config[firma][0]["hraneni"][0]["pripravnycasobsluhy"] + ", 1, " +
                            casstrojni.ToString().Replace(",", ".") + ", 1, " +
                            casobsluhy.ToString().Replace(",", ".") + ", 1, " +
                            casstrojni.ToString().Replace(",", ".") + ", 1, " +
                            casobsluhy.ToString().Replace(",", ".") + ", 1, " +
                            config[firma][0]["hraneni"][0]["pocetlidi"] + ", 1, " + config[firma][0]["hraneni"][0]["pocetstroju"] + ", " +
                            "1, 1, 1, GETDATE(), SUSER_SNAME(), NULL, NULL)";
                        helios.ExecSQL(s);

                    }
                }
                // neni to delkovy, ale existuje tam technologicky postup, tak ho smazu
                else if (!item.hraneni && match.idpostup > 0)
                {
                    helios.ExecSQL("update TabPostup set ZmenaDo = " + idzmena + " where ID = " + match.idpostup.ToString());
                }

                // delkovy material, tedy rezani
                // ---------------------------------------------------
                match = postupy.Where(x => x.idkmen == item.idkmen && x.nazev.Contains(config[firma][0]["rezani"][0]["nazev"].ToString())).FirstOrDefault(); // zjistim jestli tam je postup
                if (match == null) match = new Postup() { }; // pokud ne, pro dalsi praci jej potrebuju definovat aspon prazdny

                if (item.indexs == "D" && match.idpostup == 0)
                {
                    // vypalek neni v TP, tak ho tam pridam
                    s = "insert into TabPostup (dilec, typ, Operace, nazev, pracoviste, tarif, ZmenaOD) values (" +
                        item.idkmen + ", 1, '" +
                        config[firma][0]["rezani"][0]["operace"] + "', '" +
                        config[firma][0]["rezani"][0]["nazev"] + "', " +
                        config[firma][0]["rezani"][0]["pracoviste"] + ", " +
                        config[firma][0]["rezani"][0]["tarif"] + ", " + idzmena + ")";

                    helios.ExecSQL(s);
                }
                // jestli neni "D"elkový materiál a existuje postup, tak ten smažu
                else if (item.indexs != "D" && match.idpostup > 0)
                {
                    helios.ExecSQL("update TabPostup set ZmenaDo = " + idzmena + " where ID = " + match.idpostup.ToString());
                }

                // delkovy material, ktery ma operaci R nebo ostatni, kterym pujde vrtani
                // ---------------------------------------------------
                match = postupy.Where(x => x.idkmen == item.idkmen && x.nazev.Contains(config[firma][0]["vrtani"][0]["nazev"].ToString())).FirstOrDefault(); // zjistim jestli tam je postup
                if (match == null) match = new Postup() { };
                //MessageBox.Show(match.nazev.ToString());

                if ((item.vrtani && match.idpostup == 0) || (item.obrabeni && match.idpostup == 0 && item.indexs == "D"))
                {
                    s = "insert into TabPostup (dilec, typ, Operace, nazev, pracoviste, tarif, ZmenaOD) values (" +
                        item.idkmen + ", 1, '" +
                        config[firma][0]["vrtani"][0]["operace"] + "', '" +
                        config[firma][0]["vrtani"][0]["nazev"] + "', " +
                        config[firma][0]["vrtani"][0]["pracoviste"] + ", " +
                        config[firma][0]["vrtani"][0]["tarif"] + ", " + idzmena + ")";
                    helios.ExecSQL(s);
                }
                else if ((!item.vrtani && match.idpostup > 0 && item.indexs != "D") || (!item.vrtani && !item.obrabeni && match.idpostup > 0 && item.indexs == "D"))
                {
                    helios.ExecSQL("update TabPostup set ZmenaDo = " + idzmena + " where ID = " + match.idpostup.ToString());
                }

                // obrabeni "R" mimo D (delkovy material)
                // ---------------------------------------------------
                match = postupy.Where(x => x.idkmen == item.idkmen && x.nazev.Contains(config[firma][0]["obrabeni"][0]["nazev"].ToString())).FirstOrDefault(); // zjistim jestli tam je postup
                if (match == null) match = new Postup() { };

                if (item.obrabeni && match.idpostup == 0 && item.indexs != "D")
                {
                    s = "insert into TabPostup (dilec, typ, Operace, nazev, pracoviste, tarif, ZmenaOD) values (" +
                        item.idkmen + ", 1, '" +
                        config[firma][0]["obrabeni"][0]["operace"] + "', '" +
                        config[firma][0]["obrabeni"][0]["nazev"] + "', " +
                        config[firma][0]["obrabeni"][0]["pracoviste"] + ", " +
                        config[firma][0]["obrabeni"][0]["tarif"] + ", " + idzmena + ")";
                    helios.ExecSQL(s);
                }
                else if(!item.obrabeni && match.idpostup > 0)
                {
                    helios.ExecSQL("update TabPostup set ZmenaDo = " + idzmena + " where ID = " + match.idpostup.ToString());
                }

                // krouzeni
                // ---------------------------------------------------
                match = postupy.Where(x => x.idkmen == item.idkmen && x.nazev.Contains(config[firma][0]["krouzeni"][0]["nazev"].ToString())).FirstOrDefault(); // zjistim jestli tam je postup
                if (match == null) match = new Postup() { };
                
                if (item.krouzeni && match.idpostup == 0)
                {
                    // vypalek neni v TP, tak ho tam pridam
                    s = "insert into TabPostup (dilec, typ, Operace, nazev, pracoviste, tarif, ZmenaOD) values (" +
                        item.idkmen + ", 1, '" +
                        config[firma][0]["krouzeni"][0]["operace"] + "', '" +
                        config[firma][0]["krouzeni"][0]["nazev"] + "', " +
                        config[firma][0]["krouzeni"][0]["pracoviste"] + ", " +
                        config[firma][0]["krouzeni"][0]["tarif"] + ", " + idzmena + ")";

                    helios.ExecSQL(s);
                }
                else if (!item.krouzeni && match.idpostup > 0)
                {
                    helios.ExecSQL("update TabPostup set ZmenaDo = " + idzmena + " where ID = " + match.idpostup.ToString());
                }

                // ukosovani
                // ---------------------------------------------------
                match = postupy.Where(x => x.idkmen == item.idkmen && x.nazev.Contains(config[firma][0]["ukosovani"][0]["nazev"].ToString())).FirstOrDefault(); // zjistim jestli tam je postup
                if (match == null) match = new Postup() { };

                if (item.ukosovani && match.idpostup == 0)
                {
                    // vypalek neni v TP, tak ho tam pridam
                    s = "insert into TabPostup (dilec, typ, Operace, nazev, pracoviste, tarif, ZmenaOD) values (" +
                        item.idkmen + ", 1, '" +
                        config[firma][0]["ukosovani"][0]["operace"] + "', '" +
                        config[firma][0]["ukosovani"][0]["nazev"] + "', " +
                        config[firma][0]["ukosovani"][0]["pracoviste"] + ", " +
                        config[firma][0]["ukosovani"][0]["tarif"] + ", " + idzmena + ")";

                    helios.ExecSQL(s);
                }
                else if (!item.ukosovani && match.idpostup > 0)
                {
                    helios.ExecSQL("update TabPostup set ZmenaDo = " + idzmena + " where ID = " + match.idpostup.ToString());
                }

                // vsechno tridim, veci co jsou V+D nebo to ma operace na polotovaru
                // ---------------------------------------------------
                match = postupy.Where(x => x.idkmen == item.idkmen && x.nazev.Contains(config[firma][0]["trideni"][0]["nazev"].ToString())).FirstOrDefault(); // zjistim jestli tam je postup
                if (match == null) match = new Postup() { };
                
                if ((item.indexs == "D" || item.indexs == "V" || item.operace) && match.idpostup == 0 && item.indexs != "A")
                {
                    s = "insert into TabPostup (dilec, typ, Operace, nazev, pracoviste, tarif, ZmenaOD) values (" +
                        item.idkmen + ", 1, '" +
                        config[firma][0]["trideni"][0]["operace"] + "', '" +
                        config[firma][0]["trideni"][0]["nazev"] + "', " +
                        config[firma][0]["trideni"][0]["pracoviste"] + ", " +
                        config[firma][0]["trideni"][0]["tarif"] + ", " + idzmena + ")";
                    helios.ExecSQL(s);
                }
                // jestli je trideni na S+O+Z a nema to operaci, tak smazu
                else if (item.indexs != "D" && item.indexs != "V" && !item.operace && match.idpostup > 0)
                {
                    helios.ExecSQL("update TabPostup set ZmenaDo = " + idzmena + " where ID = " + match.idpostup.ToString());
                }

                // zpracovani SESTAV podle charakteru budu davat svařování nebo montáž nebo balení
                // NEDODELANO

                

            }

            // zjisteni jestli pod rozpracovanou zmenou jsou nejake nove radky, pokud ne, smazu, jinak zpoplatnim
            sql = helios.OpenSQL("select count(*) from TabPostup where ZmenaOD = " + idzmena + " or ZmenaDO = " + idzmena);
            if (sql.FieldValues(0) > 0)
            { 
                // zpoplatneni zmeny
                helios.ExecSQL("EXEC dbo.hpx_2JCP_zalozitZmenoveRizeni @in = " + idzmena);
            }
            else
            {
                helios.ExecSQL("delete from TabCZmeny where id = " + idzmena);
            }

            // vypisu uzivateli, ktere dilce maji rozpracovanou zmenu a tudiz nelze navaz skriptem
            sql = helios.OpenSQL("select TabPostup.dilec as idkmen, " +
                "TabPostup.typ, " +
                "isnull(TabPostup.Operace,'') as Operace, " +
                "isnull(TabPostup.nazev, '') as nazev, " +
                "isnull(TabPostup.pracoviste, 0) as idcpraco, " +
                "isnull(TabPostup.tarif,0) as idtarifh, " +
                "CONVERT(tinyint, isnull(TabCZmeny.Platnost, 0)) as Platnost, " +
                "concat(TabCZmeny.Rada, ' - ', TabCZmeny.ciszmeny, ' :: Název: ', TabCZmeny.navrh, ', Autor: ', TabCZmeny.Autor) as zmenapopis, " +
                "TabPostup.TBC, TabPostup.TBC_T, TabPostup.TBC_Obsluhy, TabPostup.TBC_Obsluhy_T, TabPostup.TAC_J, TabPostup.TAC_J_T, " +
                "TabPostup.TAC_Obsluhy_J, TabPostup.TAC_Obsluhy_J_T, TabPostup.TAC, TabPostup.TAC_T, " +
                "TabPostup.TAC_Obsluhy, TabPostup.TAC_Obsluhy_T, TabPostup.PocetLidi, TabPostup.PocetKusu, TabPostup.PocetStroju, " +
                "isnull(TabKmenZbozi.RegCis,'') as RegCis, isnull(TabKmenZbozi.SkupZbo,'') as SkupZbo \n" +
                "FROM TabPostup INNER JOIN TabCZmeny ON TabCZmeny.ID = TabPostup.ZmenaOD left join TabKmenZbozi on TabKmenZbozi.ID = TabPostup.dilec \n" +
                "where TabPostup.ZmenaDo is NULL and TabPostup.dilec in (" + idkmenin + ")");

            // 


            // vraceni puvodniho kurzoru
            Cursor.Current = Cursors.Default;
        }
    }

    internal class Kusovnik
    {
        public int idkmen { get; set; }
        public int opocet { get; set; }
        public double omax { get; set; }
        public double hmotnost { get; set; }
        public bool hraneni { get; set; }
        public bool obrabeni { get; set; }
        public bool krouzeni { get; set; }
        public bool vrtani { get; set; }
        public bool ukosovani { get; set; }
        public string indexs { get; set; }
        public bool operace { get; set; } 
        public string regcis { get; set; }
    }

    internal class Postup
    {
        public int idpostup { get; set; }
        public int idkmen { get; set; }
        public int typ { get; set; }
        public string operace { get; set; }
        public string nazev { get; set; }
        public int idcpraco { get; set; }
        public double idtarifh { get; set; } // TabPostup.tarif=TabTarH.ID
        public bool platnost { get; set; }
        public string zmenapopis { get; set;}
        public string regcis { get; set; }
        public string skupzbo { get; set;}
        public double omax { get; set; }
        public int opocet { get; set; }
        public double TBC { get; set; }
        public int TBC_T { get; set; }
        public double TBC_Obsluhy { get; set; }
        public int TBC_Obsluhy_T { get; set; }
        public double TAC_J { get; set; }
        public int TAC_J_T { get; set; }
        public double TAC_Obsluhy_J { get; set; }
        public int TAC_Obsluhy_J_T { get; set; }
        public double TAC { get; set; }
        public int TAC_T { get; set; }
        public double TAC_Obsluhy { get; set; }
        public int TAC_Obsluhy_T { get; set; }
    }
    
    internal class KusovnikRozpracovano
    {
        public string regcis { get; set; }
        public string zmenapopis { get; set; }
    }

}
