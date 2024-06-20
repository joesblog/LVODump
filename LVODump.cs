using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public class LVODump
{
  public class LVOEntry
  {
    public int Position { get; set; }
    public string MethodName { get; set; }
    public string Parameters { get; set; }
    public string Registers { get; set; }

    public short Offset { get; set; }

    public string[] arParameters => Parameters.Split(',', StringSplitOptions.RemoveEmptyEntries).ToArray();
    public string[] arRegisters => Registers.Split('/', StringSplitOptions.RemoveEmptyEntries).ToArray();

    public bool validParReg => arParameters.Length == arRegisters.Length;

    public List<Params> getParams()
    {
      return Params.get(this);
    }
    public class Params
    {
      public string paramName { get; set; }

      public Reg reg { get; set; }

      public static List<Params> get(LVOEntry ent)
      {
        if (!ent.validParReg) new List<Params>();


        List<Params> res = new List<Params>();

        for (int i = 0; i < ent.arParameters.Length; i++)
        {
          Params newParam = new Params();
          var fParam = ent.arParameters[i];
          var fReg = ent.arRegisters[i];
          int rpos = int.Parse(fReg.Substring(1));
          if (fReg.ToLower().StartsWith("a"))
          {
            newParam.reg = new rega(rpos);
          }
          else if (fReg.ToLower().StartsWith("d"))
            newParam.reg = new regd(rpos);

          newParam.paramName = fParam;

          res.Add(newParam);
        }


        return res;


      }


    }


    public class regd : Reg
    {
      public regd(int pos) : base('d', pos)
      {

      }
    }

    public class rega : Reg
    {
      public rega(int pos) : base('a', pos)
      {
      }
    }

    public class Reg
    {
      public Reg(char type, int pos)
      {
        this.type = type;
        this.pos = pos;
      }

      public char type { get; set; }
      public int pos { get; set; }

    }

  }
  public enum ErrorType
  {
    SYNTAX,
    NOFILENAME,
    FILENOTFOUND,
    NOBIAS,
    INVALIDBIAS,
    UNKNOWNDIRECTIVE
  }

  private enum ParamType
  {
    UNKNOWN_PAR,
    FILENAME_PAR,
    NOTAB_PAR,
    NOHEADER_PAR,
    NOBASE_PAR,
    NOPRIVATE_PAR
  }

  private enum DirectiveType
  {
    UNKNOWN_DIR,
    BASE_DIR,
    BIAS_DIR,
    PRIVATE_DIR,
    PUBLIC_DIR,
    END_DIR
  }


  private const int HEAD = 1;
  private const int TAIL = 2;
  private const int BIASSTEP = 6;

  private const char SPACE = ' ';
  private const char TAB = '\t';
  private const char LF = '\n';
  private const char COMMA = ',';
  private const char COLON = ':';
  private const char SLASH = '/';
  private const char POINT = '.';

  private const int MAXSTRLEN = 255;
  private const char TEMPLATEREQUEST = '?';

  private readonly string CTEMPLATE = ",";
  private readonly string TEMPLATE = "FILENAME/A,NOTAB/S,NOHEADER/S,NOBASE/S,NOPRIVATE/S: ";
  private readonly string CDIRECTIVES = ",BASE,BIAS,PRIVATE,PUBLIC,END.";
  private readonly string DIRECTIVES = "##BASE,##BIAS,##PRIVATE,##PUBLIC,##END.";
  private readonly string FNAMEEND = "_lib.fd";
  private readonly string FDPATH = "fd:";
  private readonly string BANNER = "\x1b[1mLVODump 1.0\x1b[0m © Marco Favaretto 1995 - Converted to C# joesblog.me.uk 2024 \n" +
                                   "Usage: LVODump <.fd file> [NOTAB] [NOHEADER] [NOBASE] [NOPRIVATE]";

  public string FName { get; set; }
  public long Bias;
  public string BaseName;
  public string LibraryName = String.Empty;
  public char Separator { get; }
  private bool IsPublic { get; set; }
  private bool HeaderOut;

  private string InLine;


  public bool ErrorRaised = false;


  #region options
  private bool PrintHeader;
  private bool PrintBase;
  private bool PrintPrivate;
  private bool Debug;
  #endregion
  private List<LVOEntry> lvoEntries = new List<LVOEntry>();

  public event EventHandler<ErrorType> onError;
  public LVODump(string fileName)
  {
    FName = fileName;
    Bias = long.MaxValue;
    BaseName = String.Empty;
    Separator = TAB;
    HeaderOut = false;
    IsPublic = true;

    ProcessFile();
  }

  public void RaiseError(ErrorType er)
  {
    ErrorRaised = true;
    onError?.Invoke(this, er);
    if (onError == null)
    {
      throw new Exception(er.ToString());
    }

  }

  #region formatting
  private string RemoveSpaces(string s, byte Where)
  {
    int First = 0;
    if ((Where & HEAD) == HEAD)
    {
      while (First < s.Length && (s[First] == SPACE || s[First] == TAB))
        First++;
    }

    int Last = s.Length - 1;
    if ((Where & TAIL) == TAIL)
    {
      while (Last >= First && (s[Last] == SPACE || s[Last] == TAB || s[Last] == LF))
        Last--;
    }

    return s.Substring(First, Last - First + 1);
  }

  private string GetFirstWord(ref string s, bool cut)
  {
    int z = s.IndexOf(SPACE);
    if (z == -1)
    {
      z = s.IndexOf(TAB);
      if (z == -1)
        z = s.Length;
    }

    string result = s.Substring(0, z);

    if (cut)
      s = RemoveSpaces(s.Substring(z), HEAD);

    return result;
  }

  private string Hex(long n, byte p)
  {
    const string HEXDIGITS = "0123456789ABCDEF";
    char[] s = new char[9];
    Array.Fill(s, '0');
    int z = 8;

    while (n > 0)
    {
      s[z--] = HEXDIGITS[(int)(n % 16)];
      n /= 16;
    }

    return new string(s, 9 - p, p);
  }
  #endregion

  private int CheckOption(string Pattern, string Opt)
  {
    string[] options = Pattern.Split(new[] { COMMA, SLASH, COLON, POINT });
    for (int i = 0; i < options.Length; i++)
    {
      if (options[i].Trim() == Opt.Trim('#').Trim())
        return i + 1;
    }
    return 0;
  }

  private ParamType CheckParameter(string Param)
  {
    Param = Param.ToUpper();
    int z = CheckOption(CTEMPLATE, Param);

    if (z == 0) return ParamType.UNKNOWN_PAR;

    return (ParamType)(z - 1);
  }

  private long GetBias(string s)
  {
    if (!long.TryParse(s, out long v))
      RaiseError(ErrorType.INVALIDBIAS);

    return v;
  }

  private DirectiveType CheckDirective(string Dir)
  {
    Dir = Dir.ToUpper();
    int z = CheckOption(CDIRECTIVES, Dir);

    if (z == 0)
    {
      if (Debug) Console.WriteLine($"Unknown directive: {Dir}");
      return DirectiveType.UNKNOWN_DIR;
    }

    return (DirectiveType)(z - 1);
  }

  static Regex libMatch = new Regex(@".*?(?<libname>\w+\.library).*");
  private void ProcessLine(string s)
  {
    if (Debug) Console.WriteLine($"Processing line: {s}");

    switch (s[0])
    {
      case '*':
        {

          if (LibraryName == String.Empty && libMatch.IsMatch(s))
          {
            LibraryName = libMatch.Match(s).Groups["libname"].Value;
          }

          break;
        }


      case '#':
        switch (CheckDirective(GetFirstWord(ref s, true)))
        {
          case DirectiveType.BIAS_DIR:
            Bias = GetBias(s);
            break;
          case DirectiveType.BASE_DIR:
            BaseName = s;
            break;
          case DirectiveType.PUBLIC_DIR:
            IsPublic = true;
            break;
          case DirectiveType.PRIVATE_DIR:
            IsPublic = false;
            break;
          case DirectiveType.END_DIR:
            break;
          default:
            RaiseError(ErrorType.UNKNOWNDIRECTIVE);
            break;
        }
        break;

      default:
        if (Bias == long.MaxValue)
          RaiseError(ErrorType.NOBIAS);

        if (!HeaderOut && PrintHeader)
        {
          HeaderOut = true;
          int z = FName.IndexOf(FNAMEEND);
          if (z != -1)
            lvoEntries.Add(new LVOEntry { MethodName = FName.Substring(0, z) });
          else
            lvoEntries.Add(new LVOEntry { MethodName = FName });

          lvoEntries.Add(new LVOEntry { MethodName = " Library Vectors Offsets" });

          if (!string.IsNullOrEmpty(BaseName) && PrintBase)
            lvoEntries.Add(new LVOEntry { MethodName = $"   (Base name: {BaseName})" });
          else
            lvoEntries.Add(new LVOEntry { MethodName = "" });
        }

        if (PrintPrivate || IsPublic)
        {
          string[] parts = s.Split(new[] { '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
          string methodName = parts[0].Trim();
          string parameters = parts.Length > 1 ? parts[1].Trim() : "";
          string registers = parts.Length > 2 ? parts[2].Trim() : "";
          registers = registers.Replace(",", "/");
          lvoEntries.Add(new LVOEntry
          {
            Position = (int)Bias,
            MethodName = methodName,
            Parameters = parameters,
            Registers = registers,
            Offset = (short)Bias
          });
        }

        Bias += BIASSTEP;
        break;
    }
  }


  private void ProcessFile()
  {
    lvoEntries = new List<LVOEntry>();

    try
    {
      using (StreamReader reader = new StreamReader(FName))
      {
        while ((InLine = reader.ReadLine()) != null)
        {
          ProcessLine(InLine);
        }
      }
    }
    catch (FileNotFoundException)
    {
      try
      {
        using (StreamReader reader = new StreamReader(FName + FNAMEEND))
        {
          while ((InLine = reader.ReadLine()) != null)
          {
            ProcessLine(InLine);
          }
        }
      }
      catch (FileNotFoundException)
      {
        try
        {
          using (StreamReader reader = new StreamReader(FDPATH + FName))
          {
            while ((InLine = reader.ReadLine()) != null)
            {
              ProcessLine(InLine);
            }
          }
        }
        catch (FileNotFoundException)
        {
          try
          {
            using (StreamReader reader = new StreamReader(FDPATH + FName + FNAMEEND))
            {
              while ((InLine = reader.ReadLine()) != null)
              {
                ProcessLine(InLine);
              }
            }
          }
          catch (FileNotFoundException)
          {
            RaiseError(ErrorType.FILENOTFOUND);
          }
        }
      }
    }
  }
  public List<LVOEntry> getEntries()
  {
    return lvoEntries;
  }

  public List<string> getResults()
  {
    return lvoEntries.Select(o => $"{o.Position}\t-0x{o.Offset:X4}\t{o.MethodName}\t{o.Parameters}\t{o.Registers}").ToList();

  }

}
