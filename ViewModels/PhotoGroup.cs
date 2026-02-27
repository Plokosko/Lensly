using System;
using System.Collections.ObjectModel;

namespace Lensly.ViewModels;

public class PhotoGroup : ViewModelBase
{
    public DateTime Date { get; set; }
    public string Header { get; set; } = string.Empty;
    public ObservableCollection<PhotoItem> Photos { get; } = new();
    
    public static string FormatDateHeader(DateTime date)
    {
        var today = DateTime.Today;
        var yesterday = today.AddDays(-1);
        
        if (date.Date == today)
            return "Today";
        if (date.Date == yesterday)
            return "Yesterday";
        if (date.Year == today.Year)
            return date.ToString("MMMM d");
        return date.ToString("MMMM d, yyyy");
    }
}

public class PhotoGroupComparer : System.Collections.Generic.IComparer<PhotoGroup>
{
    public int Compare(PhotoGroup? x, PhotoGroup? y)
    {
        if (x == null && y == null) return 0;
        if (x == null) return 1;
        if (y == null) return -1;
        return y.Date.CompareTo(x.Date);
    }
}
