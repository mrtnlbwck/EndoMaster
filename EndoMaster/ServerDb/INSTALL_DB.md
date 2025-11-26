# Instalacja bazy danych SinuViewer

Ten dokument opisuje sposób instalacji bazy danych PostgreSQL używanej przez aplikację SinuViewer.

Pliki bazy znajdują się w katalogu:

ServerDB/
│
├── init_sinutronic.bat # Skrypt tworzący bazę i użytkownika
└── database_init.sql # Struktura tabel + dane początkowe

---

## 1. Wymagania

- Zainstalowany PostgreSQL (np. wersja 14–16)
- Dostęp do użytkownika `postgres` (superuser)
- System Windows
- Uprawnienia do uruchamiania skryptów `.bat`

---

## 2. Zainstaluj PostgreSQL

1. Pobierz instalator PostgreSQL.
2. Zainstaluj go, zapamiętując:
   - hasło użytkownika `postgres`
   - katalog instalacji, np.: C:\Program Files\PostgreSQL\16\


3. W katalogu `bin` powinien znajdować się `psql.exe`:


---

## 3. Przygotowanie plików

Upewnij się, że obydwa pliki są w katalogu:
ServerDB/
init_sinutronic.bat
database_init.sql

Ważne: skrypt `.bat` zakłada, że **database_init.sql leży w tym samym folderze**.

---

## 4. Uruchomienie skryptu instalacyjnego

### 4.1. Otwórz CMD jako Administrator

Start → wpisz `cmd` → prawy przycisk → „Uruchom jako administrator”.

### 4.2. Przejdź do folderu ServerDB
cd /d C:\ŚCIEŻKA_DO_PROJEKTU\ServerDB

### 4.3. Wykonaj skrypt `.bat`

Format:
init_sinutronic.bat "ŚCIEŻKA_DO_POSTGRES_BIN" "HASŁO_POSTGRES" "HASŁO_DLA_SINUTRONIC"


Przykład:
init_sinutronic.bat "C:\Program Files\PostgreSQL\16\bin" "postgres123" "sinutronic123"


Co zrobi skrypt?

1. Utworzy użytkownika `sinutronic`
2. Nada mu uprawnienia `CREATEDB`
3. Utworzy bazę `sinutronic`
4. Wykona `database_init.sql` – tworząc:
   - wszystkie tabele,
   - relacje,
   - dane początkowe (roles, users, settings)

Po poprawnym wykonaniu powinieneś zobaczyć:


---

## 5. Weryfikacja instalacji

### 5.1. pgAdmin

1. Połącz się z serwerem PostgreSQL.
2. Sprawdź, czy w sekcji **Databases** istnieje `sinutronic`.
3. W `Schemas → public → Tables` powinny być m.in.:
   - patient
   - examination
   - image
   - movie
   - device
   - roles
   - users
   - settings

### 5.2. Sprawdzenie przez psql

"C:\Program Files\PostgreSQL\16\bin\psql.exe" -U sinutronic -d sinutronic


W konsoli:
\dt

---

## 6. Connection string do aplikacji

Aplikacja łączy się do bazy przez connection string w formacie:


---

## 7. Rozwiązywanie problemów

### ❗ Błąd logowania do PostgreSQL
- sprawdź poprawność hasła `postgres`
- spróbuj uruchomić CMD jako Administrator

### ❗ Błąd `psql not found`
- upewnij się, że podałeś pełną ścieżkę do folderu **bin**, np.:

"C:\Program Files\PostgreSQL\16\bin"

### ❗ Baza nie została utworzona
- sprawdź, czy port 5432 nie jest zajęty
- upewnij się, że PostgreSQL działa jako usługa

---

Gotowe!


