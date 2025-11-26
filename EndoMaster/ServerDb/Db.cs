using Npgsql;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EndoMaster.ServerDb;

public sealed class Db
{
    private readonly string _conn;

    public Db(string connectionString) => _conn = connectionString;

    public async Task<(bool ok, string? sqlState, string? message)> TryConnectAsync(CancellationToken ct = default)
    {
        try
        {
            await using var c = new NpgsqlConnection(_conn);
            await c.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("SELECT 1", c);
            await cmd.ExecuteScalarAsync(ct);
            return (true, null, null);
        }
        catch (PostgresException pex)
        {
            // np. 3D000 (brak bazy), 28P01 (złe hasło)
            return (false, pex.SqlState, pex.MessageText);
        }
        catch (NpgsqlException nex)
        {
            return (false, null, nex.Message);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    /// <summary>Tworzy minimalne tabele, jeśli nie istnieją.</summary>
    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        const string sql = @"
CREATE TABLE IF NOT EXISTS patient(
  id_patient        SERIAL PRIMARY KEY,
  name              VARCHAR(40)  NOT NULL DEFAULT ' ',
  surname           VARCHAR(50)  NOT NULL DEFAULT ' ',
  pesel             TEXT         NOT NULL,
  telephone         VARCHAR(40),
  birthdate         DATE,
  street            VARCHAR(100),
  city              VARCHAR(100),
  email             VARCHAR(100),
  vip               BOOLEAN      NOT NULL DEFAULT FALSE,
  important_counter INT          NOT NULL DEFAULT 0,
  last_exam_date    DATE
);

CREATE TABLE IF NOT EXISTS examination(
  id_examination    SERIAL PRIMARY KEY,
  id_device         INT,
  id_doctor         INT,
  date              DATE NOT NULL DEFAULT CURRENT_DATE,
  time              TIME NOT NULL DEFAULT CURRENT_TIME,
  type_of_device    TEXT,
  id_patient        INT NOT NULL REFERENCES patient(id_patient) ON DELETE CASCADE,
  type_of_exam      TEXT,
  important         BOOLEAN NOT NULL DEFAULT FALSE,
  description       TEXT,
  important_counter INT NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS image(
  id_image        SERIAL PRIMARY KEY,
  path            TEXT NOT NULL,
  id_examination  INT NOT NULL REFERENCES examination(id_examination) ON DELETE CASCADE,
  time            TIME NOT NULL DEFAULT CURRENT_TIME,
  important       BOOLEAN NOT NULL DEFAULT FALSE,
  description     TEXT,
  hue             INT,
  brightness      INT,
  contrast        INT
);

CREATE TABLE IF NOT EXISTS movie(
  id_movie        SERIAL PRIMARY KEY,
  path            TEXT NOT NULL,
  id_examination  INT NOT NULL REFERENCES examination(id_examination) ON DELETE CASCADE,
  time            TIME NOT NULL DEFAULT CURRENT_TIME,
  important       BOOLEAN NOT NULL DEFAULT FALSE,
  description     TEXT,
  hue             INT,
  brightness      INT,
  contrast        INT,
  is_filtered     BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS users(
  id             SERIAL PRIMARY KEY,
  login          TEXT UNIQUE NOT NULL,
  password_hash  TEXT NOT NULL,
  is_enabled     BOOLEAN NOT NULL DEFAULT TRUE,
  first_name     TEXT,
  last_name      TEXT,
  is_doctor      BOOLEAN NOT NULL DEFAULT FALSE,
  npwz           TEXT
);";

        await using var c = new NpgsqlConnection(_conn);
        await c.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ─── Pacjent awaryjny ──────────────────────────────────────────────────────
    public async Task<int> EnsureEmergencyPatientAsync(CancellationToken ct = default)
    {
        const string selectSql = "SELECT id_patient FROM patient WHERE name='EMERGENCY' AND surname='EMERGENCY' LIMIT 1;";
        const string insertSql = "INSERT INTO patient(name,surname,pesel) VALUES ('EMERGENCY','EMERGENCY','EMERGENCY') RETURNING id_patient;";

        await using var c = new NpgsqlConnection(_conn);
        await c.OpenAsync(ct);

        await using (var check = new NpgsqlCommand(selectSql, c))
        {
            var id = await check.ExecuteScalarAsync(ct);
            if (id is int existing) return existing;
        }

        await using var ins = new NpgsqlCommand(insertSql, c);
        var newId = (int)(await ins.ExecuteScalarAsync(ct))!;
        return newId;
    }

    // ─── Badanie ───────────────────────────────────────────────────────────────
    public async Task<int> CreateExamAsync(
        int patientId,
        string? deviceType = null,
        int? deviceId = null,
        int? doctorId = null,
        string? examType = null,
        CancellationToken ct = default)
    {
        const string sql = @"
INSERT INTO examination(id_device,id_doctor,type_of_device,id_patient,type_of_exam)
VALUES (@dev,@doc,@dtype,@pid,@etype)
RETURNING id_examination;";

        await using var c = new NpgsqlConnection(_conn);
        await c.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.AddWithValue("@dev", (object?)deviceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@doc", (object?)doctorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dtype", (object?)deviceType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pid", patientId);
        cmd.Parameters.AddWithValue("@etype", (object?)examType ?? DBNull.Value);
        var id = (int)(await cmd.ExecuteScalarAsync(ct))!;

        // update last_exam_date
        await using var up = new NpgsqlCommand("UPDATE patient SET last_exam_date = CURRENT_DATE WHERE id_patient=@p;", c);
        up.Parameters.AddWithValue("@p", patientId);
        await up.ExecuteNonQueryAsync(ct);

        return id;
    }

    // ─── Media ─────────────────────────────────────────────────────────────────
    public async Task<int> AddImageAsync(string path, int examId, CancellationToken ct = default)
    {
        const string sql = "INSERT INTO image(path,id_examination) VALUES (@p,@e) RETURNING id_image;";
        await using var c = new NpgsqlConnection(_conn);
        await c.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.AddWithValue("@p", path);
        cmd.Parameters.AddWithValue("@e", examId);
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<int> AddMovieAsync(string path, int examId, CancellationToken ct = default)
    {
        const string sql = "INSERT INTO movie(path,id_examination) VALUES (@p,@e) RETURNING id_movie;";
        await using var c = new NpgsqlConnection(_conn);
        await c.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.AddWithValue("@p", path);
        cmd.Parameters.AddWithValue("@e", examId);
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }

    // ─── Pacjent: dodanie ─────────────────────────────────────────────────────
    public async Task<int> AddPatientAsync(
        string name,
        string surname,
        string pesel,
        string? telephone = null,
        DateTime? birthdate = null,
        string? street = null,
        string? city = null,
        string? email = null,
        bool vip = false,
        CancellationToken ct = default)
    {
        const string sql = @"
INSERT INTO patient(name,surname,pesel,telephone,birthdate,street,city,email,vip)
VALUES (@name,@surname,@pesel,@tel,@birth,@street,@city,@mail,@vip)
RETURNING id_patient;";

        await using var c = new NpgsqlConnection(_conn);
        await c.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@surname", surname);
        cmd.Parameters.AddWithValue("@pesel", pesel);
        cmd.Parameters.AddWithValue("@tel", (object?)telephone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@birth", (object?)birthdate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@street", (object?)street ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@city", (object?)city ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@mail", (object?)email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@vip", vip);
        var id = (int)(await cmd.ExecuteScalarAsync(ct))!;
        return id;
    }

    // ─── Pacjent: wyszukiwanie ────────────────────────────────────────────────
    public async Task<List<(int id_patient, string name, string surname, string pesel, DateTime? last_exam_date)>> SearchPatientsAsync(
    string query,
    CancellationToken ct = default)

    {
        string sql;
        var useQuery = !string.IsNullOrWhiteSpace(query);

        if (useQuery)
            sql = @"
SELECT id_patient, name, surname, pesel, last_exam_date
FROM patient
WHERE LOWER(name) LIKE LOWER(@q)
   OR LOWER(surname) LIKE LOWER(@q)
   OR pesel LIKE @q
ORDER BY surname, name
LIMIT 50;";
        else
            sql = @"
SELECT id_patient, name, surname, pesel, last_exam_date
FROM patient
ORDER BY id_patient DESC
LIMIT 50;";


        await using var c = new NpgsqlConnection(_conn);
        await c.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, c);
        if (useQuery) cmd.Parameters.AddWithValue("@q", $"%{query}%");

        var list = new List<(int, string, string, string, DateTime?)>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var id = rd.GetInt32(0);
            var name = rd.GetString(1);
            var sur = rd.GetString(2);
            var pesel = rd.GetString(3);
            DateTime? lastExam = rd.IsDBNull(4) ? (DateTime?)null : rd.GetDateTime(4);

            list.Add((id, name, sur, pesel, lastExam));
        }

        return list;
    }

    // ─── DTO ──────────────────────────────────────────────────────────────────
    public sealed record PatientDto(
    int id_patient,
    string name,
    string surname,
    string pesel);

    public sealed record ExamDto(
        int id_exam,
        DateTime date,
        TimeSpan time,
        string? type,
        string? description,
        bool important);

    public sealed record MediaDto(
        bool isMovie,
        int id,
        TimeSpan time,
        string path,
        string? description,
        bool important);


    // ─── Odczyt pacjenta ─────────────────────────────────────────────────────
    public async Task<PatientDto> GetPatientAsync(int patientId, CancellationToken ct = default)
    {
        const string sql = @"
SELECT id_patient, name, surname, pesel
FROM patient
WHERE id_patient = @id
LIMIT 1;";

        await using var c = new NpgsqlConnection(_conn);
        await c.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.AddWithValue("@id", patientId);

        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            throw new InvalidOperationException($"Patient {patientId} not found");

        return new PatientDto(
            id_patient: rd.GetInt32(0),
            name: rd.GetString(1),
            surname: rd.GetString(2),
            pesel: rd.GetString(3));
    }

    // ─── Lista badań pacjenta ────────────────────────────────────────────────
    public async Task<List<ExamDto>> GetExamsForPatientAsync(int patientId, CancellationToken ct = default)
    {
        const string sql = @"
SELECT
  id_examination      AS id_exam,
  date,
  time,
  type_of_exam        AS type,
  description,
  important
FROM examination
WHERE id_patient = @id
ORDER BY date DESC, time DESC, id_examination DESC;";

        await using var c = new NpgsqlConnection(_conn);
        await c.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.AddWithValue("@id", patientId);

        var list = new List<ExamDto>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            list.Add(new ExamDto(
                id_exam: rd.GetInt32(0),
                date: rd.GetDateTime(1),
                time: rd.GetTimeSpan(2),
                type: rd.IsDBNull(3) ? null : rd.GetString(3),
                description: rd.IsDBNull(4) ? null : rd.GetString(4),
                important: rd.GetBoolean(5)));
        }

        return list;
    }

    // ─── Media dla badania ───────────────────────────────────────────────────
    public async Task<List<MediaDto>> GetMediaForExamAsync(int examId, CancellationToken ct = default)
    {
        const string sql = @"
SELECT
  FALSE              AS is_movie,
  id_image           AS id,
  time,
  path,
  description,
  important
FROM image
WHERE id_examination = @e

UNION ALL

SELECT
  TRUE               AS is_movie,
  id_movie           AS id,
  time,
  path,
  description,
  important
FROM movie
WHERE id_examination = @e

ORDER BY time;";

        await using var c = new NpgsqlConnection(_conn);
        await c.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.AddWithValue("@e", examId);

        var list = new List<MediaDto>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            list.Add(new MediaDto(
                isMovie: rd.GetBoolean(0),
                id: rd.GetInt32(1),
                time: rd.GetTimeSpan(2),
                path: rd.GetString(3),
                description: rd.IsDBNull(4) ? null : rd.GetString(4),
                important: rd.GetBoolean(5)));
        }

        return list;
    }

    // ─── Media: aktualizacja / usuwanie ───────────────────────────────────────
    public async Task UpdateMediaAsync(
        bool isMovie,
        int id,
        string? description,
        bool important,
        CancellationToken ct = default)
    {
        string table = isMovie ? "movie" : "image";
        string pk = isMovie ? "id_movie" : "id_image";
        string sql = $"UPDATE {table} SET description=@d, important=@i WHERE {pk}=@id;";

        await using var c = new NpgsqlConnection(_conn);
        await c.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.AddWithValue("@d", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@i", important);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteMediaAsync(
        bool isMovie,
        int id,
        CancellationToken ct = default)
    {
        string table = isMovie ? "movie" : "image";
        string pk = isMovie ? "id_movie" : "id_image";
        string sql = $"DELETE FROM {table} WHERE {pk}=@id;";

        await using var c = new NpgsqlConnection(_conn);
        await c.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ─── Badanie: aktualizacja opisu ──────────────────────────────────────────
    public async Task UpdateExamDescriptionAsync(
        int examId,
        string? description,
        CancellationToken ct = default)
    {
        const string sql = "UPDATE examination SET description = @d WHERE id_examination = @id;";

        await using var c = new NpgsqlConnection(_conn);
        await c.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.AddWithValue("@d", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", examId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ─── Badanie: usuwanie całego badania ─────────────────────────────────────
    public async Task DeleteExamAsync(int examId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM examination WHERE id_examination = @id;";

        await using var c = new NpgsqlConnection(_conn);
        await c.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.AddWithValue("@id", examId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    //public async Task SetMediaImportantAsync(bool isMovie, int id, bool important, CancellationToken ct = default)
    //{
    //    string sql = isMovie
    //        ? "UPDATE movie SET important = @i WHERE id_movie = @id;"
    //        : "UPDATE image SET important = @i WHERE id_image = @id;";

    //    await using var c = new NpgsqlConnection(_conn);
    //    await c.OpenAsync(ct);

    //    await using var cmd = new NpgsqlCommand(sql, c);
    //    cmd.Parameters.AddWithValue("@i", important);
    //    cmd.Parameters.AddWithValue("@id", id);

    //    await cmd.ExecuteNonQueryAsync(ct);
    //}


}
