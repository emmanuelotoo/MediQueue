using MediQueue.Domain.Entities;
using MediQueue.Shared.Authorization;

namespace MediQueue.Infrastructure.Seed;

/// <summary>
/// The fixed cast of the demo hospital: its departments and its staff.
/// Separated from the generator so the interesting code stays readable.
/// </summary>
internal static class DemoData
{
    /// <summary>Password for every seeded account. Development only.</summary>
    internal const string StaffPassword = "MediQueue#2026";

    internal static readonly Department[] Departments =
    [
        new() { Name = "General Outpatient", Code = "GEN", ConsultationRooms = 4, DefaultServiceMinutes = 12 },
        new() { Name = "Cardiology", Code = "CAR", ConsultationRooms = 2, DefaultServiceMinutes = 22 },
        new() { Name = "Paediatrics", Code = "PED", ConsultationRooms = 3, DefaultServiceMinutes = 15 },
        new() { Name = "Obstetrics & Gynaecology", Code = "OBG", ConsultationRooms = 2, DefaultServiceMinutes = 20 },
        new() { Name = "Ophthalmology", Code = "EYE", ConsultationRooms = 2, DefaultServiceMinutes = 14 },
        new() { Name = "Dental", Code = "DEN", ConsultationRooms = 2, DefaultServiceMinutes = 25 }
    ];

    internal record StaffSeed(string FullName, string Email, string Role, string? DepartmentCode);

    internal static readonly StaffSeed[] Staff =
    [
        new("Wiafe Franklin Asare", "franklin.asare@mediqueue.gh", Roles.Admin, null),
        new("Elsie Atsu", "elsie.atsu@mediqueue.gh", Roles.Admin, null),

        new("Sangeerth Shyjith", "sangeerth.shyjith@mediqueue.gh", Roles.Receptionist, null),
        new("Ivan Johnson", "ivan.johnson@mediqueue.gh", Roles.Receptionist, null),
        new("Emmanuel Thisara Otoo", "emmanuel.otoo@mediqueue.gh", Roles.Receptionist, null),

        new("Abdul-Aziz Naeem", "abdulaziz.naeem@mediqueue.gh", Roles.Clinician, "GEN"),
        new("Odoi Nii Anang", "nii.anang@mediqueue.gh", Roles.Clinician, "CAR"),
        new("Derrick Debrah", "derrick.debrah@mediqueue.gh", Roles.Clinician, "PED"),
        new("Moses Kwame Mensah", "moses.mensah@mediqueue.gh", Roles.Clinician, "OBG"),
        new("Jeremiah Kwadwo Wiafe", "jeremiah.wiafe@mediqueue.gh", Roles.Clinician, "EYE"),
        new("Osman Umar Farouk", "umar.farouk@mediqueue.gh", Roles.Clinician, "DEN")
    ];

    internal static readonly string[] FirstNames =
    [
        "Kwame", "Ama", "Kofi", "Akosua", "Yaw", "Abena", "Kwabena", "Adwoa", "Kwaku", "Afua",
        "Kojo", "Esi", "Fiifi", "Araba", "Nana", "Efua", "Kwesi", "Akua", "Yaa", "Mensah",
        "Selorm", "Elikem", "Dela", "Sedem", "Mawuli", "Fatima", "Ibrahim", "Amina", "Musah", "Zainab",
        "Emmanuel", "Gifty", "Prince", "Comfort", "Isaac", "Grace", "Samuel", "Priscilla", "Daniel", "Sandra"
    ];

    internal static readonly string[] LastNames =
    [
        "Mensah", "Owusu", "Boateng", "Asante", "Addo", "Appiah", "Ofori", "Darko", "Amoah", "Baidoo",
        "Quartey", "Tetteh", "Lamptey", "Nortey", "Sowah", "Agyeman", "Ansah", "Bediako", "Frimpong", "Gyasi",
        "Adjei", "Anyidoho", "Kudzo", "Abban", "Yeboah", "Osei", "Antwi", "Danso", "Larbi", "Nkrumah"
    ];

    internal static readonly string[] Genders = ["Female", "Male"];
}
