using AssetDashboard.Domain;

namespace AssetDashboard.Infrastructure.Seeding;

/// <summary>
/// Reference data for the synthetic portfolio. All manufacturer and company names are fictional.
/// Price ranges are rough list prices for new equipment, in EUR.
/// </summary>
public static class AssetCatalog
{
    public sealed record Category(string Name, decimal MinCost, decimal MaxCost, int MaxFleetSize);

    public sealed record ClassInfo(
        AssetClass AssetClass,
        double PortfolioWeight,
        Category[] Categories,
        string[] Manufacturers,
        int[] TermsMonths,
        (ProductType Type, double Weight)[] Products,
        (RemarketingChannel Channel, double Weight)[] Channels,
        string VendorNoun,
        string CustomerNoun);

    public static readonly ClassInfo[] Classes =
    [
        new(AssetClass.Agriculture, 0.25,
            [new("Tractor", 60_000, 350_000, 3), new("Combine Harvester", 250_000, 750_000, 1),
             new("Self-propelled Sprayer", 120_000, 400_000, 1), new("Baler", 25_000, 120_000, 2),
             new("Milking Robot", 120_000, 250_000, 4)],
            ["Harvestar", "Polder Agritech", "Greenfield Machinery", "Nordic Tillage"],
            [36, 48, 60, 72, 84],
            [(ProductType.Loan, 0.40), (ProductType.FinanceLease, 0.35), (ProductType.HirePurchase, 0.25)],
            [(RemarketingChannel.LiveAuction, 0.45), (RemarketingChannel.OnlineAuction, 0.35), (RemarketingChannel.PrivateSale, 0.20)],
            "Agri Equipment", "Farms"),

        new(AssetClass.Construction, 0.20,
            [new("Crawler Excavator", 80_000, 600_000, 3), new("Wheel Loader", 100_000, 450_000, 2),
             new("Mobile Crane", 300_000, 1_200_000, 1), new("Backhoe Loader", 70_000, 150_000, 3),
             new("Concrete Pump", 150_000, 500_000, 1)],
            ["Ironclad Heavy", "Terraform Machines", "Rhine Earthmoving", "Brightwater Equipment"],
            [36, 48, 60, 72],
            [(ProductType.FinanceLease, 0.45), (ProductType.Loan, 0.30), (ProductType.HirePurchase, 0.25)],
            [(RemarketingChannel.LiveAuction, 0.50), (RemarketingChannel.OnlineAuction, 0.35), (RemarketingChannel.PrivateSale, 0.15)],
            "Construction Machinery", "Bouw & Infra"),

        new(AssetClass.Transportation, 0.18,
            [new("Tractor Unit", 90_000, 160_000, 6), new("Refrigerated Trailer", 40_000, 90_000, 6),
             new("Delivery Van", 35_000, 75_000, 10), new("City Bus", 250_000, 550_000, 4),
             new("Electric Truck", 200_000, 400_000, 3)],
            ["Longhaul Motors", "Meridian Motors", "Kestrel Coachworks", "Voltway Trucks"],
            [36, 48, 60],
            [(ProductType.HirePurchase, 0.40), (ProductType.FinanceLease, 0.35), (ProductType.OperatingLease, 0.25)],
            [(RemarketingChannel.OnlineAuction, 0.50), (RemarketingChannel.PrivateSale, 0.30), (RemarketingChannel.LiveAuction, 0.20)],
            "Truck & Trailer", "Logistics"),

        new(AssetClass.Technology, 0.12,
            [new("Server Cluster", 50_000, 500_000, 1), new("Storage Array", 40_000, 300_000, 2),
             new("Network Core Switch", 20_000, 150_000, 4), new("Laptop Fleet", 30_000, 200_000, 1),
             new("Production Printing System", 15_000, 120_000, 3)],
            ["Northgate Systems", "Quantix", "Datavault", "Corelink"],
            [24, 36, 48],
            [(ProductType.OperatingLease, 0.60), (ProductType.FinanceLease, 0.40)],
            [(RemarketingChannel.VendorBuyBack, 0.50), (RemarketingChannel.PrivateSale, 0.35), (RemarketingChannel.OnlineAuction, 0.15)],
            "IT Solutions", "Digital Services"),

        new(AssetClass.Healthcare, 0.10,
            [new("MRI Scanner", 900_000, 2_500_000, 1), new("CT Scanner", 400_000, 1_500_000, 1),
             new("Ultrasound System", 30_000, 200_000, 4), new("Digital X-Ray System", 80_000, 400_000, 2),
             new("Dental Treatment Unit", 20_000, 60_000, 6)],
            ["Medivista", "Lumen Imaging", "Helix Diagnostics", "Sanacore"],
            [48, 60, 72, 84],
            [(ProductType.FinanceLease, 0.50), (ProductType.OperatingLease, 0.30), (ProductType.Loan, 0.20)],
            [(RemarketingChannel.VendorBuyBack, 0.45), (RemarketingChannel.PrivateSale, 0.45), (RemarketingChannel.OnlineAuction, 0.10)],
            "Medical Systems", "Medical Centre"),

        new(AssetClass.MaterialHandling, 0.10,
            [new("Counterbalance Forklift", 20_000, 60_000, 8), new("Reach Truck", 35_000, 80_000, 6),
             new("Aerial Work Platform", 20_000, 120_000, 5), new("Automated Storage System", 200_000, 1_500_000, 1)],
            ["Stackwise", "Palletron", "Hoistline", "Liftora"],
            [36, 48, 60],
            [(ProductType.OperatingLease, 0.45), (ProductType.FinanceLease, 0.35), (ProductType.HirePurchase, 0.20)],
            [(RemarketingChannel.OnlineAuction, 0.50), (RemarketingChannel.PrivateSale, 0.30), (RemarketingChannel.LiveAuction, 0.20)],
            "Intralogistics", "Warehousing"),

        new(AssetClass.CleanTech, 0.05,
            [new("Solar PV Installation", 100_000, 2_000_000, 1), new("Battery Storage System", 150_000, 1_500_000, 1),
             new("EV Charging Hub", 20_000, 150_000, 6), new("Industrial Heat Pump", 30_000, 200_000, 2)],
            ["Sunfield Energy", "Voltaic Storage", "Gridleaf", "Amperra"],
            [60, 84, 120],
            [(ProductType.Loan, 0.50), (ProductType.FinanceLease, 0.50)],
            [(RemarketingChannel.PrivateSale, 0.60), (RemarketingChannel.VendorBuyBack, 0.25), (RemarketingChannel.OnlineAuction, 0.15)],
            "Energy Solutions", "Energy"),
    ];

    public static ClassInfo For(AssetClass assetClass) => Classes.Single(c => c.AssetClass == assetClass);

    public sealed record CountryInfo(string Code, double Weight, string LegalForm, string[] Cities, string[] Surnames);

    public static readonly CountryInfo[] Countries =
    [
        new("NL", 0.28, "B.V.", ["Amsterdam", "Rotterdam", "Utrecht", "Eindhoven", "Groningen", "Zwolle", "Venlo"],
            ["de Vries", "Jansen", "Bakker", "Visser", "Smit", "Meijer", "Mulder", "de Boer"]),
        new("DE", 0.22, "GmbH", ["Hamburg", "Munich", "Cologne", "Hannover", "Stuttgart", "Leipzig", "Münster"],
            ["Müller", "Schmidt", "Schneider", "Fischer", "Weber", "Becker", "Hoffmann", "Krüger"]),
        new("BE", 0.10, "NV", ["Antwerp", "Ghent", "Brussels", "Liège", "Bruges"],
            ["Peeters", "Janssens", "Maes", "Claes", "Wouters", "Dubois"]),
        new("FR", 0.14, "SAS", ["Lyon", "Lille", "Nantes", "Toulouse", "Bordeaux", "Rennes"],
            ["Martin", "Bernard", "Moreau", "Laurent", "Lefebvre", "Girard"]),
        new("IT", 0.08, "S.r.l.", ["Milan", "Bologna", "Verona", "Turin", "Parma"],
            ["Rossi", "Ferrari", "Esposito", "Bianchi", "Romano", "Colombo"]),
        new("ES", 0.07, "S.L.", ["Madrid", "Valencia", "Zaragoza", "Seville", "Murcia"],
            ["García", "Fernández", "López", "Martínez", "Sánchez", "Pérez"]),
        new("IE", 0.05, "Ltd", ["Dublin", "Cork", "Galway", "Limerick"],
            ["Murphy", "Kelly", "O'Sullivan", "Walsh", "Byrne", "Ryan"]),
        new("AT", 0.06, "GmbH", ["Vienna", "Linz", "Graz", "Salzburg"],
            ["Gruber", "Huber", "Wagner", "Pichler", "Steiner", "Moser"]),
    ];
}
