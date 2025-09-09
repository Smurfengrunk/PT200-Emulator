# PT200 Emulator

En C#-baserad emulator för PT200-terminalen, med fokus på korrekt tangentbordsinmatning, visuell layout och protokollhantering.

## 🧩 Funktioner

- Emulering av PT200-terminalens beteende
- Tolkar kontrolltecken och escape-sekvenser
- Hanterar EMACS-läge och promptmarkering
- Visar terminaldata i WPF-baserad UI
- TCP-baserad kommunikation med server

## 🛠️ Projektstruktur

- `Core/` – Terminalens logik, buffer, state och rendering
- `IO/` – Inmatning via tangentbord och nätverk
- `Models/` – Parser- och sekvensmodeller
- `Protocol/` – Telnet-specifik hantering
- `UI/` – WPF-gränssnitt och visningslogik
- `Old/` – Avvecklade komponenter för referens

## 🚀 Kom igång

1. Klona projektet  
2. Öppna i Visual Studio  
3. Kör `MainWindow.xaml.cs`  
4. Anslut till server via TCP

## 📦 Byggstatus

Projektet är för tillfället på paus, då det blivit för rörigt och svåröverskådligt.
Ny version kommer så småningom.

## 📜 Licens

Projektet är licensierat under MIT (eller valfri licens du vill använda).
