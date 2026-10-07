namespace GoatFinder.Core;

public sealed class NavigationHistory
{
    private readonly List<string> _items = [];
    private int _index = -1;

    public string? Current => _index >= 0 ? _items[_index] : null;
    public bool CanGoBack => _index > 0;
    public bool CanGoForward => _index >= 0 && _index < _items.Count - 1;

    public void Visit(string path)
    {
        if (string.Equals(Current, path, StringComparison.OrdinalIgnoreCase)) return;

        // Navegar para um novo lugar descarta o "futuro" do histórico, como num navegador.
        if (_index < _items.Count - 1) _items.RemoveRange(_index + 1, _items.Count - _index - 1);
        _items.Add(path);
        _index = _items.Count - 1;
    }

    public string? Back()
    {
        if (!CanGoBack) return null;
        _index--;
        return Current;
    }

    public string? Forward()
    {
        if (!CanGoForward) return null;
        _index++;
        return Current;
    }
}
