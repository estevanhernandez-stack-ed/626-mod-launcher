# Rule: danger-filled buttons must survive the visual states

## The trap

A `Style` (even `BasedOn="{StaticResource DefaultButtonStyle}"`) that sets
`Background="{StaticResource ThemeDanger}"` only wins AT REST. The stock
Button template's PointerOver/Pressed visual states re-resolve
`ButtonBackgroundPointerOver` / `ButtonBackgroundPressed` via `ThemeResource`
and overwrite the fill the moment the pointer arrives. The button reads
danger until you reach for it — exactly backwards.

This shipped once (vibe-glow F-037: the safe-clear primary) and was caught by
review, not tests — the App layer is headless-untestable.

## The pattern

Element-scope the state keys on the button itself, using the SAME live brush
instances `ThemeService.Apply` mutates (never new brushes — they'd freeze the
color at injection time):

```csharp
var res = Application.Current.Resources;
button.Resources["ButtonBackgroundPointerOver"] = res["ThemeDanger"];
button.Resources["ButtonBackgroundPressed"]     = res["ThemeDanger"];
button.Resources["ButtonForegroundPointerOver"] = res["ThemeBg"];
button.Resources["ButtonForegroundPressed"]     = res["ThemeBg"];
```

For a ContentDialog primary button, the template part is named
`PrimaryButton` and only exists once the popup tree is built — hook the
**Title content's `Loaded`** (never `Opened`; it races the popup wiring in
both directions — see `DialogTheming`). Reference implementation:
`src/ModManager.App/SafeClearDialog.xaml.cs`.

## Scope

Filled danger is only sanctioned inside a confirm dialog (design language
button rules). Anywhere it appears, this pattern rides along.
