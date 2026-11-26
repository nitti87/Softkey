using System.Windows.Media;

// Beskriver en tangent på tangentbordet.
//   Label = texten på tangenten      Scan = tangentens scancode
//   W     = bredd (1 = normal)       Ext  = utökad tangent (piltangenter, Win m.fl.)
//   Ch    = tecknet för bokstäver (används av ordförslagen)
//   Mod   = true för Sft/Ctl/Alt/AltGr/Win (ligger kvar tills nästa tangent)
//   Fg    = textfärg                 Vk   = virtuell tangentkod (bara för Pause)
record KeyDef(string Label, ushort Scan, double W = 1, bool Ext = false, char Ch = '\0',
              bool Mod = false, Brush Fg = null, ushort Vk = 0);
