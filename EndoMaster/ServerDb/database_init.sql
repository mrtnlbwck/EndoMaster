SET datestyle = 'German,DMY'; --to set better date format
CREATE TABLE  patient (
	id_patient serial NOT NULL PRIMARY KEY,
	name varchar(40) ,
	surname varchar(50) ,
	pesel varchar(100),
	telephone varchar(40),
	birthdate date,
	street varchar(100),
	city varchar(100),
	email varchar(100),
	vip boolean NOT NULL DEFAULT false,
    important_counter integer DEFAULT 0,
    last_exam_date date
	);

CREATE TABLE roles (
    id SERIAL PRIMARY KEY,
    name VARCHAR(255) UNIQUE NOT NULL
);


CREATE TABLE users (
    id SERIAL PRIMARY KEY,
    login VARCHAR(255) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    first_name VARCHAR(255),
    last_name VARCHAR(255),
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    is_enabled BOOLEAN NOT NULL DEFAULT true,
    role_id INTEGER REFERENCES roles(id), -- klucz obcy do tabeli roles
    is_doctor boolean NOT NULL DEFAULT true,
    npwz character varying(20) --numer prawa wykonywania zawodu
);

CREATE TABLE  device (
	id_device serial NOT NULL PRIMARY KEY,
	name varchar(100), --systemowa
	res_x  int,
	res_y  int,
	type varchar(100) DEFAULT 'inne', --np. Otoskop,Endoskop,Szpatułka,Szpatułka FHD
	fps int,
    hue double precision DEFAULT 0,
    brightness double precision DEFAULT 125.5,
    contrast double precision DEFAULT 135.5,
    video_length int DEFAULT 10,
    properties jsonb
);

CREATE TABLE  examination
(
	id_examination
	    serial NOT NULL PRIMARY KEY,
	id_device smallint references device ON DELETE RESTRICT,
	id_doctor smallint references users ON DELETE SET NULL ,
	date date DEFAULT CURRENT_DATE,
	time time DEFAULT LOCALTIME(0),
    type_of_device varchar(50), --np. Otoskop,Endoskop,Szpatułka,Szpatułka FHD
	id_patient integer references patient ON DELETE CASCADE,
	type_of_exam varchar(50), --Ucho lub Krtań lub Nosogardło lub Fiberoskopia
    important boolean NOT NULL DEFAULT false,
    description text,
    important_counter integer DEFAULT 0

);

CREATE TABLE  movie(
id_movie serial PRIMARY KEY,
path varchar(300) NOT NULL,
id_examination
    serial references examination
        ON DELETE CASCADE,
time time DEFAULT LOCALTIME(0),
important boolean NOT NULL DEFAULT false,
description text,
hue double precision DEFAULT 0,
brightness double precision DEFAULT 125.5,
contrast double precision DEFAULT 135.5,
is_filtered boolean DEFAULT false
);

CREATE TABLE  image(
id_image serial NOT NULL PRIMARY KEY,
path varchar(300) NOT NULL,
id_examination
    serial references examination
        ON DELETE CASCADE,
time time DEFAULT LOCALTIME(0),
important boolean NOT NULL DEFAULT false,
description text,
hue double precision DEFAULT 0,
brightness double precision DEFAULT 125.5,
contrast double precision DEFAULT 135.5

);



CREATE TABLE settings
(
    setting_key VARCHAR(20) UNIQUE NOT NULL,
    setting_value VARCHAR(255) ,
    setting_name VARCHAR(40) ,
    setting_description VARCHAR(1024) ,
    CONSTRAINT settings_pkey PRIMARY KEY (setting_key)
);



--INSERT INTO aparat (nazwa, res_x, res_y, typ, fps) VALUES ();

INSERT INTO roles (name) VALUES ('administrator'), ('lekarz');
INSERT INTO users (login, password_hash, first_name, last_name, is_enabled, role_id) VALUES
('lekarz1', '$2b$12$mqSQ02.59K6s5JcTdXBX7uUAOk4jlcaBwWtcHn5CD1BQay/EzqH3y', 'lekarz1', 'Sinutronic', true, (SELECT id FROM roles WHERE name = 'administrator')),
('sinutronic', '$2b$12$GOqQXQspV//NvkXLrvp3EOEWPjkyLCyFjg5VeSijxIUC2lGs1.jke', 'sinutronic', 'Sinutronic', true, (SELECT id FROM roles WHERE name = 'lekarz'));

INSERT INTO settings(
	setting_key, setting_value, setting_name, setting_description)
	VALUES ('dev_mode', '1', 'Developer Mode', 'w przypadku gdy 1 - tryb włączony 0 - wyłączony'),
    ('color_def', '#E7E7E7', 'Domyślny kolor', 'Domyślny kolor lewego frama dla customizacji'),
    ('color_cur', '#E7E7E7', 'Obecny kolor', 'Obecny kolor lewego frama dla customizacji'),
    ('table_def', '1110110', 'Domyślna postać tabeli pacjentów', '1-oznacza występowanie, 0 -oznacza brak; [0]-imie, [1]-nazwisko,' ||
                                                               '[2]-pesel, [3]-data ostatniego badania, [4]-data urodzenia, [5]-nr telefonu, [6]-email'),
    ('table_cur', '1110110', 'Obecna postać tabeli pacjentów', '1-oznacza występowanie, 0 -oznacza brak; [0]-imie, [1]-nazwisko,' ||
                                                               '[2]-pesel, [3]-data ostatniego badania, [4]-data urodzenia, [5]-nr telefonu, [6]-email'),
    ('logged_in_user', 'none', 'Zalogowany użytkownik', 'Obecnie zalgwany użytkownik - login, jeśli none, to brak zalogowanego użytkownika');
