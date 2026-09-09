# Selected native method map

Addresses are native entry VAs recovered from VB6 metadata, event-link thunks, procedure layout, and FuncDesc/vtable metadata.

## File

| VB6 method | VA | vtable |
|---|---:|---:|
| Appendi | 0x0048D200 | 0x710 |
| Log | 0x0048D330 | 0x714 |
| Scrivi | 0x0048D850 | 0x718 |
| ApriFile | 0x0048D980 | 0x71C |
| GetIni | 0x0048DDF0 | 0x720 |
| GetFile | 0x0048E510 | 0x724 |
| SoloExt | 0x00490370 | 0x738 |
| SoloNomeFile | 0x00490540 | 0x73C |
| SoloPath | 0x004909A0 | 0x740 |
| WriteIni | 0x00490BF0 | 0x744 |
| Esiste | 0x004911F0 | 0x748 |

`GetIni` parameters recovered from typeinfo: `Sezione, Variabile, Default, Filename`.

`WriteIni` parameters recovered from typeinfo: `Appname, KeyName, keydefault, Filename`.

## Funzioni

| VB6 method | VA | vtable |
|---|---:|---:|
| LoadCodicePerTipo | 0x004990E0 | 0x7A0 |
| NuovoDocumento | 0x0049A650 | 0x7A4 |
| RecordToReport | 0x0049E260 | 0x7BC |
| AddObjects | 0x0049EE60 | 0x7C0 |
| CaricaDocumento | 0x004A0600 | 0x7C4 |
| Delay | 0x004A06D0 | 0x7C8 |
| ExecuteSql | 0x004A23D0 | 0x7E8 |
| Opendataset | 0x004A65C0 | 0x814 |
| CheckPassword | 0x004AF750 | 0x824 |
| GetVariabile | 0x004AFEF0 | 0x82C |
| AddProg | 0x004B6420 | 0x848 |
| Vbscript | 0x004B7D00 | 0x84C |
| Login | 0x004BDC40 | 0x89C |

## frmCarta

| VB6 method | VA | vtable |
|---|---:|---:|
| Marcatutti | 0x0052BB40 | 0x7BC |
| SelectField | 0x0052CC50 | 0x7C0 |
| NuovoCampo | 0x0052E5D0 | 0x7C4 |
| LabelToCampo | 0x0053A670 | 0x7CC |
| CampoToLabel | 0x0053BA20 | 0x7D0 |
| CampoToIni | 0x0053D790 | 0x7D4 |
| IniToCampo | 0x0053F7E0 | 0x7D8 |
| DisegnaCampi | 0x00541FB0 | 0x7DC |
| AggiornaMisure | 0x00549930 | 0x7E0 |
| SetupCampo | 0x0054B050 | 0x7E4 |
| SetButton | 0x0054E660 | 0x7E8 |
| DisegnaRighello | 0x0054EA20 | 0x7EC |
| AggiornaMarcatori | 0x00550A30 | 0x7F0 |
| FaiFoto | 0x00551B20 | 0x7F4 |
| AggiornaBottoni | 0x005534A0 | 0x7F8 |
| Record2Card | 0x00553FA0 | 0x7FC |

## MainForm

| VB6 method | VA | vtable |
|---|---:|---:|
| MDIForm_Load | 0x0059C7A0 | event |
| mnuFilePrint_Click | 0x005B2A40 | 0x7D8 |
| StampaRecord | 0x005B3830 | 0x7DC |
| CaricaSfondo | 0x005B5650 | 0x7E0 |
| mnuFileSaveAs_Click | 0x005B5D90 | event/private |
| mnuFileSave_Click | 0x005B6360 | 0x7E4 |
| mnuFileOpen_Click | 0x005B7550 | event/private |
| SetButton | 0x005B8490 | 0x7E8 |
| SetCoordinate | 0x005B8850 | 0x7EC |
| AdattaCarta | 0x005B8B50 | 0x7F0 |
| PrinterEscape | 0x005B9150 | 0x7F4 |
| Termina | 0x005B9450 | 0x7F8 |
| PuoFare | 0x005B9AB0 | 0x7FC |
| SetPrivilegi | 0x005BA8B0 | 0x800 |
| GestioneRecord | 0x005BC580 | 0x804 |
| GestioneSequenza | 0x005BDF00 | 0x808 |
| GestioneLato | 0x005BECE0 | 0x80C |
| GestioneCampo | 0x005BFDB0 | 0x810 |
| VersioneDemo | 0x005BFE30 | 0x814 |

## Global layout lifecycle helper

`0x00601EA0` — central routine called by layout Open/Save/SaveAs and job/layout workflows. It is not assigned a trustworthy VB source name yet.
