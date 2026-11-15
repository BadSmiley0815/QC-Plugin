// Die Testprogramme unter tools\ prüfen die fertige 99-2_client.dll und brauchen dafür Zugriff auf interne Klassen.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Vorschau")]
[assembly: InternalsVisibleTo("RtspTest")]
[assembly: InternalsVisibleTo("DecoderTest")]
[assembly: InternalsVisibleTo("KameraTest")]
