namespace InPatient.Api.Data.Entities;

// Marks scaffolded entities as tenant-owned. The scaffolded `int HospitalId` property satisfies the interface.
public partial class Admission : ITenantEntity { }
public partial class AdmissionWardStay : ITenantEntity { }
public partial class AiSummary : ITenantEntity { }
public partial class Alert : ITenantEntity { }
public partial class Bed : ITenantEntity { }
public partial class BedEvent : ITenantEntity { }
public partial class CareTask : ITenantEntity { }
public partial class ClinicalNote : ITenantEntity { }
public partial class ConsultantWard : ITenantEntity { }
public partial class DoseTask : ITenantEntity { }
public partial class LabResult : ITenantEntity { }
public partial class NurseShift : ITenantEntity { }
public partial class NurseShiftBed : ITenantEntity { }
public partial class Patient : ITenantEntity { }
public partial class Prescription : ITenantEntity { }
public partial class PrescriptionItem : ITenantEntity { }
public partial class StoredFile : ITenantEntity { }
public partial class VitalSign : ITenantEntity { }
public partial class VoiceCapture : ITenantEntity { }
public partial class Ward : ITenantEntity { }
public partial class VwPatientWardJourney : ITenantEntity { }
public partial class VwWardHistory : ITenantEntity { }
public partial class VwWardOccupancyNow : ITenantEntity { }
