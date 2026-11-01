using System.Globalization;
using System.IO;
using SurfaceNotebook.Models;

namespace SurfaceNotebook.Services;

public sealed record CircuitPart(string From, string To, double Value);
public sealed record DcCircuit(CircuitPart[] Resistors, CircuitPart[] VoltageSources, CircuitPart[] CurrentSources, string Positive, string Negative);

// Modified nodal analysis for finite, connected DC networks with independent sources.
// Current source direction is From -> To; voltage source value is V(From)-V(To).
public static class DcCircuitSolver
{
    private static string F(double x) => x.ToString("G6", CultureInfo.InvariantCulture);
    public static (string Answer, SolutionStep[] Steps) Solve(DcCircuit c)
    {
        if (c.Resistors is null || c.VoltageSources is null || c.CurrentSources is null || c.VoltageSources.Length+c.CurrentSources.Length>6) throw new InvalidDataException("Unsupported or incomplete circuit graph.");
        var all = c.Resistors.Concat(c.VoltageSources).Concat(c.CurrentSources).ToArray();
        if (all.Length == 0 || all.Length > 8 || all.Any(p => p is null || string.IsNullOrWhiteSpace(p.From) || string.IsNullOrWhiteSpace(p.To) || p.From.Length > 8 || p.To.Length > 8 || !p.From.All(char.IsLetterOrDigit) || !p.To.All(char.IsLetterOrDigit) || p.From == p.To || !double.IsFinite(p.Value)) || c.Resistors.Any(p => p.Value <= 0)) throw new InvalidDataException("Invalid DC circuit. Check the component values and connections.");
        var nodes = all.SelectMany(p => new[] { p.From, p.To }).Distinct().Where(n => n != "0").ToArray();
        if (nodes.Length > 6 || !all.Any(p => p.From == "0" || p.To == "0") || !nodes.Append("0").Contains(c.Positive) || !nodes.Append("0").Contains(c.Negative)) throw new InvalidDataException("The circuit needs a reference node and valid output nodes.");
        int N(string name) => Array.IndexOf(nodes, name);
        double[] Run(int active = -1)
        {
            int n = nodes.Length, size = n + c.VoltageSources.Length;
            var a = new double[size, size + 1];
            void Add(int i, int j, double value) { if (i >= 0 && j >= 0) a[i,j] += value; }
            foreach (var r in c.Resistors) { int x=N(r.From), y=N(r.To); double g=1/r.Value; Add(x,x,g); Add(y,y,g); Add(x,y,-g); Add(y,x,-g); }
            for (int k=0;k<c.VoltageSources.Length;k++) { var v=c.VoltageSources[k]; int x=N(v.From),y=N(v.To),j=n+k; Add(x,j,1); Add(y,j,-1); Add(j,x,1); Add(j,y,-1); a[j,size]=active < 0 || active == k ? v.Value : 0; }
            for (int k=0;k<c.CurrentSources.Length;k++) { var v=c.CurrentSources[k]; if (active >= 0 && active != c.VoltageSources.Length+k) continue; int x=N(v.From),y=N(v.To); Add(x,size,-v.Value); Add(y,size,v.Value); }
            var original=(double[,])a.Clone();
            for (int k=0;k<size;k++) {
                int pivot=k; for(int i=k+1;i<size;i++) if(Math.Abs(a[i,k])>Math.Abs(a[pivot,k])) pivot=i;
                if(Math.Abs(a[pivot,k])<1e-12) throw new InvalidDataException("The interpreted circuit is floating or inconsistent. Check the connections.");
                for(int j=k;j<=size;j++) (a[k,j],a[pivot,j])=(a[pivot,j],a[k,j]);
                double d=a[k,k]; for(int j=k;j<=size;j++) a[k,j]/=d;
                for(int i=0;i<size;i++) if(i!=k) { double m=a[i,k]; for(int j=k;j<=size;j++) a[i,j]-=m*a[k,j]; }
            }
            var result=Enumerable.Range(0,size).Select(i=>a[i,size]).ToArray();
            for(int i=0;i<size;i++) { double sum=0,scale=1+Math.Abs(original[i,size]); for(int j=0;j<size;j++){sum+=original[i,j]*result[j];scale+=Math.Abs(original[i,j]*result[j]);} if(!double.IsFinite(sum)||Math.Abs(sum-original[i,size])>1e-8*scale) throw new InvalidDataException("Circuit residual check failed."); }
            return result;
        }
        double V(double[] values,string node) => node=="0" ? 0 : values[N(node)];
        var solved=Run(); double output=V(solved,c.Positive)-V(solved,c.Negative);
        var steps=new List<SolutionStep>();
        steps.Add(new("Check the interpreted connections", "Node 0 is ground. Each pair below names the component endpoints; current arrows run from the first node to the second.", all.Select(p=>$"\\text{{{(c.Resistors.Contains(p)?"R":c.VoltageSources.Contains(p)?"V source":"I source")}: {p.From} to {p.To}}} = {F(p.Value)}").Take(8).ToArray()));
        foreach(var node in nodes.Where(node=>!c.VoltageSources.Any(v=>v.From==node||v.To==node))) {
            var terms=c.Resistors.Where(r=>r.From==node||r.To==node).Select(r=>$"\\frac{{V_{{{node}}}-V_{{{(r.From==node?r.To:r.From)}}}}}{{{F(r.Value)}}}").ToList();
            double incoming=c.CurrentSources.Sum(i=>(i.To==node?i.Value:0)-(i.From==node?i.Value:0));
            steps.Add(new($"Node method: node {node}", "Sum the resistor currents leaving this node and equate them to the net source current entering it.", [string.Join(" + ",terms)+" = "+F(incoming)]));
        }
        steps.Add(new("Solve the node equations", "The app solves the circuit equations and checks their residuals. Output voltage is measured from the positive output node to the negative output node.", nodes.Select((node,i)=>$"V_{{{node}}} = {F(solved[i])}\\,\\mathrm{{V}}").Append($"V_0 = {F(output)}\\,\\mathrm{{V}}").ToArray()));
        var contributions=new List<double>();
        for(int k=0;k<c.VoltageSources.Length+c.CurrentSources.Length;k++) { var values=Run(k); double contribution=V(values,c.Positive)-V(values,c.Negative); contributions.Add(contribution); steps.Add(new($"Superposition: source {k+1} alone", "Keep this source active. Short all other independent voltage sources and open all other independent current sources.", [$"V_{{0,{k+1}}} = {F(contribution)}\\,\\mathrm{{V}}"])); }
        if(Math.Abs(contributions.Sum()-output)>1e-7*(1+Math.Abs(output))) throw new InvalidDataException("Node and superposition results disagree.");
        steps.Add(new("Combine and check", "The source contributions agree with the full node solution. This checks the calculation for the interpreted circuit, not the image transcription.", [$"V_0 = {string.Join(" + ",contributions.Select(x=>"("+F(x)+")"))} = {F(output)}\\,\\mathrm{{V}}"]));
        return ($"V_0 = {F(output)}\\,\\mathrm{{V}}",steps.ToArray());
    }
}
