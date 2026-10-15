namespace HdrPilot.App.Infrastructure.Effects;

/// <summary>Ein Lichtpartikel des HDR-Moments (Position in px, Geschwindigkeit in px/s, Lebensdauer in s).</summary>
public struct Particle
{
    public double X;
    public double Y;
    public double VelocityX;
    public double VelocityY;
    public double Radius;
    public double Age;
    public double Life;

    /// <summary>Index in die Farbliste des Moments.</summary>
    public int Color;

    /// <summary>Deckkraft nach Alter: schnell ein (erste 15 %), dann langsam aus (Prototyp tick()).</summary>
    public readonly double Alpha
    {
        get
        {
            var k = Life <= 0 ? 1 : Math.Clamp(Age / Life, 0, 1);
            return k < 0.15 ? k / 0.15 : 1 - ((k - 0.15) / 0.85);
        }
    }

    /// <summary>Sichtbarer Radius (wächst mit dem Alter um bis zu 40 %); gezeichnet wird ein Lichthof vom Dreifachen.</summary>
    public readonly double DrawRadius => Radius * (1 + (Math.Clamp(Life <= 0 ? 1 : Age / Life, 0, 1) * 0.4));
}

/// <summary>
/// Partikel des HDR-Moments (G10.6) als reine Rechnung, ohne Zeichnen: ein Ausbruch am Spitzlicht, dann Funken entlang des
/// Lichtstreifens und vereinzelt am Spitzlicht. Höchstens <see cref="EffectPolicy.MaxParticles"/> gleichzeitig; nach
/// <see cref="EffectPolicy.HdrMomentDuration"/> ist alles verloschen (die letzte halbe Sekunde blendet aus), danach
/// meldet sich der Spieler vom Bildtakt ab. Zufall von außen (Tests und Standbilder: fester Startwert).
/// </summary>
public sealed class ParticleSystem
{
    /// <summary>Partikel des ersten Ausbruchs am Spitzlicht.</summary>
    public const int BurstCount = 46;

    /// <summary>So lange laufen Funken mit dem Lichtstreifen (Sekunden).</summary>
    public const double SweepSeconds = 1.5;

    /// <summary>Funken je Sekunde entlang des Streifens.</summary>
    public const double SweepRate = 120;

    /// <summary>Funken je Sekunde am Spitzlicht nach dem Streifen.</summary>
    public const double TrailRate = 20;

    /// <summary>Letzter Abschnitt vor dem Ende, in dem alles ausblendet (Sekunden).</summary>
    public const double FadeOutSeconds = 0.5;

    private readonly Random _random;
    private readonly List<Particle> _particles = new(EffectPolicy.MaxParticles);
    private readonly int _colors;
    private double _spawnDebt;

    public ParticleSystem(Random random, int colors)
    {
        _random = random;
        _colors = Math.Max(1, colors);
    }

    /// <summary>Breite und Höhe der Zeichenfläche (px).</summary>
    public double Width { get; private set; }

    public double Height { get; private set; }

    /// <summary>Spitzlicht (Ursprung des Ausbruchs) in Koordinaten der Zeichenfläche.</summary>
    public double OriginX { get; private set; }

    public double OriginY { get; private set; }

    /// <summary>Zeit seit dem Start (Sekunden).</summary>
    public double Elapsed { get; private set; }

    public IReadOnlyList<Particle> Particles => _particles;

    /// <summary>Höchste Anzahl gleichzeitiger Partikel in diesem Lauf (Prüfung der Obergrenze).</summary>
    public int Peak { get; private set; }

    /// <summary>Vorbei: Laufzeit erreicht, nichts mehr zu zeichnen.</summary>
    public bool Finished => Elapsed >= EffectPolicy.HdrMomentDuration.TotalSeconds;

    /// <summary>Gemeinsame Ausblendung der letzten halben Sekunde (1 vorher, 0 am Ende).</summary>
    public double FadeOut => Math.Clamp((EffectPolicy.HdrMomentDuration.TotalSeconds - Elapsed) / FadeOutSeconds, 0, 1);

    /// <summary>Neuer Lauf: Fläche, Spitzlicht, erster Ausbruch.</summary>
    public void Start(double width, double height, double originX, double originY)
    {
        _particles.Clear();
        _spawnDebt = 0;
        Elapsed = 0;
        Peak = 0;
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        OriginX = originX;
        OriginY = originY;
        for (var i = 0; i < BurstCount; i++)
        {
            Spawn(originX + ((_random.NextDouble() - 0.5) * 120), originY + ((_random.NextDouble() - 0.5) * 70), big: true);
        }
    }

    /// <summary>
    /// Einen Schritt weiter (dt in Sekunden, je Bild höchstens 0,05 s wie im Prototyp, damit ein ruckelndes Bild nichts
    /// überspringt). Liefert false, sobald der Lauf vorbei ist.
    /// </summary>
    public bool Step(double dt)
    {
        if (Finished)
        {
            _particles.Clear();
            return false;
        }

        dt = Math.Clamp(double.IsFinite(dt) ? dt : 0, 0, 0.05);
        Elapsed += dt;
        Emit(dt);

        for (var i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                _particles.RemoveAt(i);
                continue;
            }

            p.X += p.VelocityX * dt;
            p.Y += p.VelocityY * dt;
            p.VelocityY += 12 * dt;
            p.VelocityX *= 0.99;
            _particles[i] = p;
        }

        if (Finished)
        {
            _particles.Clear();
            return false;
        }

        return true;
    }

    private void Emit(double dt)
    {
        if (Elapsed >= EffectPolicy.HdrMomentDuration.TotalSeconds - FadeOutSeconds)
        {
            return; // in der Ausblendung keine neuen Funken mehr
        }

        var sweep = Elapsed < SweepSeconds;
        _spawnDebt += dt * (sweep ? SweepRate : TrailRate);
        while (_spawnDebt >= 1)
        {
            _spawnDebt -= 1;
            if (sweep)
            {
                // Mit dem Lichtstreifen von links nach rechts über die Fläche.
                var k = Elapsed / SweepSeconds;
                var x = (-0.2 + (k * 1.5)) * Width;
                Spawn(x + ((_random.NextDouble() - 0.5) * 140), _random.NextDouble() * Height, _random.NextDouble() < 0.4);
            }
            else
            {
                Spawn(OriginX + ((_random.NextDouble() - 0.5) * Width * 0.3), OriginY + ((_random.NextDouble() - 0.3) * Height * 0.5), big: false);
            }
        }
    }

    private void Spawn(double x, double y, bool big)
    {
        if (_particles.Count >= EffectPolicy.MaxParticles)
        {
            return;
        }

        var angle = _random.NextDouble() * Math.PI * 2;
        var speed = big ? 40 + (_random.NextDouble() * 160) : 10 + (_random.NextDouble() * 40);
        _particles.Add(new Particle
        {
            X = x,
            Y = y,
            VelocityX = (Math.Cos(angle) * speed * 0.8) + 18,
            VelocityY = (Math.Sin(angle) * speed * 0.6) - 40 - (_random.NextDouble() * 50),
            Radius = big ? 1.2 + (_random.NextDouble() * 3.2) : 0.8 + (_random.NextDouble() * 2),
            Life = 1.1 + (_random.NextDouble() * 1.6),
            Color = _random.Next(_colors),
        });
        Peak = Math.Max(Peak, _particles.Count);
    }
}
