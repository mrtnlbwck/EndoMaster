# SinuViewer


Projekt jest rozwijany w technologii **WinUI 3 / Windows App SDK** oraz wykorzystuje bazę danych **PostgreSQL**.

---

## 1. Wymagania systemowe

- Windows 10 lub 11 (64-bit)
- Zainstalowany .NET SDK (w wersji zgodnej z projektem, np. .NET 8)
- Visual Studio 2022 (zalecane)
- Windows App SDK / WinUI 3 (zgodnie z projektem)
- Zainstalowany PostgreSQL (instalacja bazy opisana w osobnym dokumencie)

---

## 2. Struktura projektu
SinuViewer/
│
├── SinuViewer/ # Źródła aplikacji WinUI
├── ServerDB/ # Pliki inicjalizujące bazę danych
│ ├── init_sinutronic.bat
│ └── database_init.sql
│
└── README.md

---

## 3. Funkcjonalności aplikacji

- Zarządzanie pacjentami  
  - lista pacjentów  
  - dane kontaktowe  
  - historia badań  
  - szybkie badania „EMERGENCY”

- Zarządzanie badaniami  
  - rejestracja zdjęć i filmów  
  - opis badania  
  - automatyczne przypisanie dat i godzin  

---

## 4. Konfiguracja połączenia z bazą

Aplikacja korzysta z bazy PostgreSQL. Po jej zainstalowaniu ustaw **connection string** np.:
Host=localhost;Port=5432;Database=sinutronic;Username=sinutronic;Password=TWOJE_HASLO;

Pliki do instalacji bazy znajdują się w:  
/ServerDB

Instrukcje instalacji opisane są w dokumencie:
INSTALL_DB.md
---

## 5. Budowanie i uruchamianie projektu

### Uruchomienie z Visual Studio
1. Otwórz `SinuViewer.sln`.
2. Upewnij się, że środowisko ma poprawne SDK WinUI.
3. Ustaw projekt `SinuViewer` jako startowy.
4. Uruchom (F5).

### Uruchomienie z gotowego builda
1. Zbuduj projekt w konfiguracji **Release**.
2. Uruchom plik `.exe` z katalogu `bin/Release/...`.

---

## 6. Dalsze informacje

Instalacja bazy → patrz: **INSTALL_DB.md**  
Pliki bazy → katalog: **ServerDB**


