using System.Collections.Concurrent;
using System.Json;

Repository r = new Repository (@"c:\SnCopy\test.json");

SourceVersion? s = r.BestSource ();
if (s is null)
{
    Console.WriteLine ("No source found");
    return;
}

CopySession c = new CopySession (r, s);
c.Cache = r.BestCache (s);

Task.WaitAll (c.Execute ());

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

        _Destination = new DirectoryInfo (@"c:\SnCopy\Destination\");
        _Source = new DirectoryInfo (@"c:\SnCopy\Source\");
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
                Console.WriteLine ($"{v.Name} - {v.FullName}");
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

    public CopySession (Repository Repository, SourceVersion Source)
    {
        _Repository = Repository;
        this.Source = Source;
    }

    public async Task Execute ()
    {
        Console.WriteLine ($"Begin Execute {Source}");
        DestinationVersion d = _Repository.CreateDestination (Source);
        Console.WriteLine ($"Begin Execute {d.FullName}");
        await Task.Delay (2000);
        Console.WriteLine ("Done Execute");
    }
}
public class Version
{
    public string Tag { get; private init; }
    private DirectoryInfo _Location;

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
}

public class DestinationVersion : Version
{
    public DestinationVersion (DirectoryInfo Location)
        : base (Location)
    {
    }
}
