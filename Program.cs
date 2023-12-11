using System.Collections.Concurrent;
using System.Json;

public class UserInterface
{
    public static void Main ()
    {
        try
        {
            Repository r = new Repository (@"c:\SnCopy\test.json");

            SourceVersion? s = Select (r.Sources (), "Please select the source version");
            if (s is null)
            {
                return;
            }
            CopySession c = new CopySession (r, s);
            c.Cache = Select (r.Caches (s), "Please select a previous version to use as prefetch");

            Task.WaitAll (c.Execute ());
        }
        catch (Exception e)
        {
            Console.WriteLine (e.Message);
        }
    }

    public static T? Select<T> (IEnumerable<T> Options, string Prompt, int Max = 5)
    {
        List<T> Values = new (Options);
        if (Max > Values.Count)
        {
            Max = Values.Count;
        }

        if (Max == 0)
        {
            return default(T);
        }

        if (Max == 1)
        {
            return Values[0];
        }

        Console.WriteLine (Prompt);
        while (true)
        {
            for (int i = 0; i < Max; i++)
            {
                Console.WriteLine ($"({i+1}) - {Values[i]}");
            }
            Console.WriteLine ($"Enter the value from 1 to {Max}, or 0 to select nothing");
            string? s = Console.ReadLine();
            if (s is null)
            {
                return default(T);
            }

            if (int.TryParse (s.Trim(), out int n))
            {
                if (n == 0)
                {
                    return default(T);
                }
                if (n >= 1 && n <= Max)
                {
                    return Values[n-1];
                }
            }
            Console.WriteLine("I did not understand");
        }
    }
}

public abstract class Filter<T>
{
    public static readonly Filter<T> True = new TrueFilter<T> ();
    public static readonly Filter<T> False = new FalseFilter<T> ();

    public abstract bool Pass (T data);

    public virtual Filter<T> And (Filter<T> Other)
    {
        return new AndFilter<T> (this, Other);
    }

    public virtual Filter<T> Or (Filter<T> Other)
    {
        return new OrFilter<T> (this, Other);
    }

    public virtual Filter<T> Not ()
    {
        return new NotFilter<T> (this);
    }
}

public class StringFilter
{
    public static Filter<string> Parse (string Expression)
    {
        Filter<string> Result = Filter<string>.False;

        foreach (string t in Expression.Split (';'))
        {
            Result = Result.Or (ParseTerm (t.Trim()));
        }
        return Result;
    }

    public static Filter<string> ParseTerm (string Term)
    {
        Filter<string> Result = Filter<string>.True;

        foreach (string t in Term.Split (' '))
        {
            Result = Result.Or (ParseFactor (t.Trim()));
        }
        return Result;
    }

    public static Filter<string> ParseFactor(string Term)
    {
        if (Term.StartsWith ("^"))
        {
            return new StartsFilter (Term[1..]);
        }
        else if (Term.EndsWith ("$"))
        {
            return new EndsFilter (Term[..^1]);
        }
        else
        {
            return new ContainsFilter (Term);
        }
    }

    public static Filter<string> ParseIncludeExclude (string Include, string Exclude)
    {
        if (String.IsNullOrEmpty (Include))
        {
            if (String.IsNullOrEmpty (Exclude))
            {
                return Filter<string>.True;
            }
            else
            {
                return Parse (Exclude).Not ();
            }
        }
        else if (String.IsNullOrEmpty (Exclude))
        {
            return Parse (Include);
        }
        else
        {
            return Parse (Include).And (Parse(Exclude).Not ());
        }
    }
}

public class TrueFilter<T> : Filter<T>
{
    public override bool Pass(T data)
    {
        return true;
    }
}

public class FalseFilter<T> : Filter<T>
{
    public override bool Pass(T data)
    {
        return false;
    }

    public override Filter<T> Or(Filter<T> Other)
    {
        return Other;
    }

    public override Filter<T> And(Filter<T> Other)
    {
        return this;
    }
}

public class NotFilter<T> : Filter<T>
{
    private Filter<T> _Inner;

    public NotFilter (Filter<T> Inner)
    {
        _Inner = Inner;
    }

    public override bool Pass(T data)
    {
        return !_Inner.Pass(data);
    }

    public override Filter<T> Not()
    {
        return _Inner;
    }
}

public class OrFilter<T> : Filter<T>
{
    private Filter<T> _Inner1, _Inner2;

    public OrFilter (Filter<T> Inner1, Filter<T> Inner2)
    {
        _Inner1 = Inner1;
        _Inner2 = Inner2;
    }

    public override bool Pass(T data)
    {
        return _Inner1.Pass(data) || _Inner2.Pass(data);
    }
}

public class AndFilter<T> : Filter<T>
{
    private Filter<T> _Inner1, _Inner2;

    public AndFilter (Filter<T> Inner1, Filter<T> Inner2)
    {
        _Inner1 = Inner1;
        _Inner2 = Inner2;
    }

    public override bool Pass(T data)
    {
        return _Inner1.Pass(data) && _Inner2.Pass(data);
    }
}

public class EqualityFilter<T> : Filter<T>
{
    private T _Value;

    public EqualityFilter (T Value)
    {
        _Value = Value;
    }

    public override bool Pass(T data)
    {
        return (data is not null) && data.Equals (_Value);
    }
}

public class ContainsFilter : Filter<string>
{
    private string _Value;

    public ContainsFilter (string Value)
    {
        _Value = Value;
    }

    public override bool Pass(string data)
    {
        return data.Contains(_Value);
    }
}

public class StartsFilter : Filter<string>
{
    private string _Value;

    public StartsFilter (string Value)
    {
        _Value = Value;
    }

    public override bool Pass(string data)
    {
        return data.StartsWith(_Value);
    }
}

public class EndsFilter : Filter<string>
{
    private string _Value;

    public EndsFilter (string Value)
    {
        _Value = Value;
    }

    public override bool Pass(string data)
    {
        return data.EndsWith(_Value);
    }
}

public class FileComparison
{
    public FileInfo Source { get; private init; }
    public FileInfo Destination { get; private init; }

    public FileComparison (FileInfo Source, FileInfo Destination)
    {
        this.Source = Source;
        this.Destination = Destination;
    }

    public bool SameSize => Source.Length == Destination.Length;
    public bool SameTime => Source.LastWriteTime == Destination.LastWriteTime;
    public bool Older => Source.LastWriteTime < Destination.LastWriteTime;

}
//   A repository is a collection of subdirectories, each representing a version
//   of a given artifact.

public class Repository
{
    private DirectoryInfo _Destination;
    private List<DestinationVersion>? _Destinations;
    private DirectoryInfo _Source;
    private List<SourceVersion>? _Sources;
    private JsonObject _Configuration;

    public Repository (string ConfigurationFile)
    {
        _Configuration = (JsonObject)JsonObject.Parse (File.ReadAllText (ConfigurationFile));

        _Source = new DirectoryInfo (_Configuration["source"]);
        _Destination = new DirectoryInfo (_Configuration["destination"]);
    }

    private T? Latest<T> (IEnumerable<T> Items, Func<T,bool> Filter) where T : Version
    {
        foreach (T v in Items)
        {
            if (Filter(v))
            {
                // No need to check for date time as the entries are sorted...
                return v;
            }
        }
        return null;
    }

    private IEnumerable<T> Enumerate<T> (DirectoryInfo Base, Func<DirectoryInfo, T> Factory, ref List<T>? Cache) where T : Version
    {
        if (Cache is null)
        {
            Cache = new ();
            foreach (DirectoryInfo v in Base.GetDirectories ())
            {
                Cache.Add (Factory (v));
            }
            Cache.Sort ((v1, v2) => v2.CreationTime.CompareTo (v1.CreationTime));
        }
        return Cache;
    }

    public IEnumerable<SourceVersion> Sources ()
    {
        return Enumerate (_Source, (v) => new SourceVersion (v), ref _Sources);
    }

    public SourceVersion? BestSource ()
    {
        return Latest (Sources(), (s) => true);
    }

    public IEnumerable<DestinationVersion> Destinations ()
    {
        return Enumerate (_Destination, (v) => new DestinationVersion (v), ref _Destinations);
    }

    public IEnumerable<DestinationVersion> Caches (SourceVersion SourceVersion)
    {
        return Destinations().Where((t) => t.Tag != SourceVersion.Tag);
    }

    public DestinationVersion? BestCache (SourceVersion SourceVersion)
    {
        return Latest (Destinations (), (t) => t.Tag != SourceVersion.Tag);
    }

    public DestinationVersion CreateDestination (SourceVersion Source)
    {
        string d = Path.Combine (_Destination.FullName, Source.Name);
        DirectoryInfo di = Directory.CreateDirectory (d);
        Directory.SetCreationTime (d, Source.CreationTime);
        return new DestinationVersion (di);
    }
}

public class AsyncQueue<T>
{
    private ConcurrentQueue<T> _Store = new ();
    private bool _Closed = false;
    public void Push (T Item)
    {
        if (_Closed)
        {
            throw new InvalidOperationException ("Cannot push to a closed AsyncQueue");
        }
        _Store.Enqueue (Item);
    }

    public void Close ()
    {
        _Closed = true;
    }

    public T? TryPop ()
    {
        if (_Store.TryDequeue (out var Item))
        {
            return Item;
        }
        return default(T);
    }

    public async Task<T?> Pop ()
    {
        while (true)
        {
            T? Item = TryPop ();
            if (Item is not null)
            {
                return Item;
            }
            else if (_Closed)
            {
                return default(T);
            }
            await Task.Delay (1000);
        }
    }
}

public class TaskQueue : AsyncQueue<Func<Task>>
{
    public async Task ExecuteAll ()
    {
        while (true)
        {
            Func<Task>? Job = await Pop ();
            if (Job is null)
            {
                return;
            }
            await (Job ());
        }
    }
}

public class CopySession
{
    private Repository _Repository;

    public SourceVersion Source { get; private init; }
    public DestinationVersion? Cache { get; set; }
    public DestinationVersion Destination { get; private init; }

    public int RemoteFilesFound { get; private set; } = 0;
    public long RemoteBytesFound { get; private set; } = 0;
    public int RemoteFilesCopied { get; private set; } = 0;
    public long RemoteBytesCopied { get; private set; } = 0;
    public int CachedFilesCopied { get; private set; } = 0;
    public long CachedBytesCopied { get; private set; } = 0;
    public int LocalFilesReused { get; private set; } = 0;
    public long LocalBytesReused { get; private set; } = 0;

    private TaskQueue _SourceFiles = new ();
    private TaskQueue _LocalCopies = new ();
    private TaskQueue _RemoteCopies = new ();
    public  DateTime Start { get; private set; } = DateTime.Now;
    private string Estimation = "(est.)";

    public CopySession (Repository Repository, SourceVersion Source)
    {
        _Repository = Repository;
        this.Source = Source;
        this.Destination = Repository.CreateDestination (Source);
    }

    private void DisplayStatisticsLine (string Label, int Files, long Bytes)
    {
        Console.WriteLine ($"{Label+":", -25} {Files, 8} {Bytes, 24:n0}                       ");
    }

    public void DisplayStatistics ()
    {
        Console.SetCursorPosition (0, 0);
        try
        {
            Console.WriteLine ("                          -Files-- -----------------Bytes--");
            DisplayStatisticsLine ("Remote Files Found", RemoteFilesFound, RemoteBytesFound);
            DisplayStatisticsLine ("Remote Files Copied", RemoteFilesCopied,RemoteBytesCopied);
            DisplayStatisticsLine ("Cached Files Copied", CachedFilesCopied, CachedBytesCopied);
            DisplayStatisticsLine ("Local Files Reused", LocalFilesReused, LocalBytesReused);
            Console.WriteLine ("                          -------- ------------------------");
            DisplayStatisticsLine ("Total", RemoteFilesCopied+CachedFilesCopied+LocalFilesReused, RemoteBytesCopied+CachedBytesCopied+LocalBytesReused);
            DisplayStatisticsLine ("Left", RemoteFilesFound-RemoteFilesCopied-CachedFilesCopied-LocalFilesReused, RemoteBytesFound-RemoteBytesCopied-CachedBytesCopied-LocalBytesReused);

            double Percents = (RemoteBytesCopied+CachedBytesCopied + 1.0) * 100.0 / (RemoteBytesFound - LocalBytesReused + 1.0);
            TimeSpan Elapsed = DateTime.Now - Start;
            TimeSpan Expected = new ((long)(Elapsed.Ticks * 100 / Percents));
            TimeSpan Left = Expected - Elapsed;
            DateTime Eta = Start + Expected;

            Console.WriteLine ();
            Console.WriteLine ($"Performed:    {(int)Percents} % {Estimation}               ");
            Console.WriteLine ($"Time elapsed: {Elapsed} {Estimation}               ");
            Console.WriteLine ($"Time left:    {Left} {Estimation}               ");
            Console.WriteLine ($"ETA:          {Eta} {Estimation}              ");
        }
        catch
        {
            // We cannot crash here...
        }
    }

    public async Task DisplayStatisticsContinuously (CancellationToken t)
    {
        Console.Clear ();
        while (!t.IsCancellationRequested)
        {
            DisplayStatistics ();
            await Task.Delay (5000);
        }
        DisplayStatistics ();
    }

    private bool CanReuse (FileInfo DestinationFile, FileInfo SourceFile)
    {
        FileComparison f = new (SourceFile, DestinationFile);
        return f.SameTime && f.SameSize;
    }

    private Task CopyFile (FileInfo File, string RelativeDirectory, string RelativeFile, bool IsCached)
    {
        return Task.Run (() =>
        {
            DirectoryInfo TargetDirectory = new (Path.Combine (Destination.FullName, RelativeDirectory));
            TargetDirectory.Create ();
            File.CopyTo (Path.Combine (Destination.FullName, RelativeFile), true);
            if (IsCached)
            {
                lock (this)
                {
                    CachedFilesCopied++;
                    CachedBytesCopied += File.Length;
                }
            }
            else
            {
                lock (this)
                {
                    RemoteFilesCopied++;
                    RemoteBytesCopied += File.Length;
                }
            }
        });
    }

    private Task Discriminate (FileInfo File, string RelativeDirectory, string RelativeFile)
    {
        return Task.Run (() =>
        {
            lock (this)
            {
                RemoteFilesFound++;
                RemoteBytesFound += File.Length;
            }

            FileInfo? DestinationFile = Destination.GetFile (File, RelativeDirectory, RelativeFile);
            if (DestinationFile is not null && CanReuse (DestinationFile, File))
            {
                lock (this)
                {
                    LocalFilesReused++;
                    LocalBytesReused += File.Length;
                }
                return;
            }

            FileInfo? CachedFile = Cache?.GetFile (File, RelativeDirectory, RelativeFile);
            if (CachedFile is not null && CanReuse (CachedFile, File))
            {
                _LocalCopies.Push (() => CopyFile (CachedFile, RelativeDirectory, RelativeFile, true));
            }
            else
            {
                _RemoteCopies.Push (() => CopyFile (File, RelativeDirectory, RelativeFile, false));
            }
        }
        );
    }

    private void Visit (FileInfo File, string RelativeDirectory, string RelativeFile)
    {
        _SourceFiles.Push (() => Discriminate (File, RelativeDirectory, RelativeFile));
    }

    private void VisitDone ()
    {
        _SourceFiles.Close ();
        Estimation = "";
    }

    private async Task DiscriminateFiles ()
    {
        await _SourceFiles.ExecuteAll ();
        _LocalCopies.Close ();
        _RemoteCopies.Close ();
    }

    public async Task Execute ()
    {
        DestinationVersion d = _Repository.CreateDestination (Source);
        CancellationTokenSource TokenSource = new ();
        Task Dashboard = DisplayStatisticsContinuously (TokenSource.Token);

        await Task.WhenAll
        (
            Task.Run (() => { Source.EnumerateFiles (Visit); VisitDone (); }),
            DiscriminateFiles (),
            _LocalCopies.ExecuteAll (),
            _RemoteCopies.ExecuteAll ()
        );

        TokenSource.Cancel ();
        await Task.WhenAll (Dashboard);
    }
}
public class Version
{
    public string Tag { get; private init; }
    protected DirectoryInfo _Location;

    public DateTime CreationTime => _Location.CreationTime;
    public string Name => _Location.Name;
    public string FullName => _Location.FullName;

    protected Version (DirectoryInfo Location)
    {
        _Location = Location;
        //  The tag is lowercased do we don't mess things up when using
        //  a Linux client to access a Windows server...
        Tag = Location.Name.ToLower();
    }

    public override string ToString()
    {
        return _Location.Name;
    }
}

public class SourceVersion : Version
{
    public SourceVersion (DirectoryInfo Location)
        : base (Location)
    {
    }

    public void EnumerateFiles (Action<FileInfo,string,string> Visitor)
    {
        EnumerateFiles (Visitor, _Location, "");
    }

    public void EnumerateFiles (Action<FileInfo,string,string> Visitor, DirectoryInfo Directory, string RelativeDirPath)
    {
        if (RelativeDirPath.StartsWith ("."))
        {
            return;
        }
        foreach (var d in Directory.GetDirectories ())
        {
            EnumerateFiles (Visitor, d, Path.Combine (RelativeDirPath, d.Name));
        }

        foreach (var f in Directory.GetFiles ())
        {
            Visitor (f, RelativeDirPath, Path.Combine (RelativeDirPath, f.Name));
        }
    }
}

public class DestinationVersion : Version
{
    public DestinationVersion (DirectoryInfo Location)
        : base (Location)
    {
    }

    public FileInfo? GetFile (FileInfo File, string RelativeDirectory, string RelativeFile)
    {
        FileInfo f = new (Path.Combine (_Location.FullName, RelativeFile));
        if (f.Exists)
        {
            return f;
        }
        return null;
    }
}
