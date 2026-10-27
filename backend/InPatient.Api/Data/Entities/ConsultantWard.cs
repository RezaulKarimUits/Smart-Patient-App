using System;
using System.Collections.Generic;

namespace InPatient.Api.Data.Entities;

public partial class ConsultantWard
{
    public int ConsultantUserId { get; set; }

    public int WardId { get; set; }

    public int HospitalId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual AppUser ConsultantUser { get; set; } = null!;

    public virtual Hospital Hospital { get; set; } = null!;

    public virtual Ward Ward { get; set; } = null!;
}
