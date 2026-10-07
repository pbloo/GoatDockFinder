using System;

namespace Goat.Shared.Windows;

public class JanelaInfo
{
    public IntPtr Hwnd { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string CaminhoExecutavel { get; set; } = string.Empty;
    public string NomeProcesso { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public bool EstaAtiva { get; set; }
    public bool EstaMinimizada { get; set; }
}
