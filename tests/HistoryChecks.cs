using NivalisMods.HudOverhaul;

internal static class HistoryChecks
{
    internal static void Run(Action<string,bool> check)
    {
        var dir=Path.Combine(Path.GetTempPath(),"hud-history-"+Guid.NewGuid()); Directory.CreateDirectory(dir);
        var path=Path.Combine(dir,"history.log");
        HistoryEvent Sale(long qty,string venue="v",int day=1)=>new() { Kind="meal_sale",Venue=venue,Subject="dish",Quantity=qty,Money=qty*20,Day=day };
        try
        {
            using(var db=new HistoryStore(path))
            {
                check("unknown saves start independent history",!db.Fork("unknown"));
                db.Append(new[]{Sale(2),Sale(3,"other"),Sale(4,"v",2)}); db.Checkpoint("A","slotA");
                db.Append(new[]{Sale(50)}); db.Checkpoint("B","slotB");
                check("old checkpoint can be resumed",db.Fork("A"));
                check("old-save branch excludes later sales",db.Read("meal_sale","v").Sum(e=>e.Quantity)==6);
                check("venue and period filters stay separate",db.Read("meal_sale","v",1,2).Sum(e=>e.Quantity)==2);
                db.Append(new[]{Sale(7)}); db.Checkpoint("C","slotC");
                db.Fork("B"); check("other branch keeps its own future",db.Read("meal_sale","v").Sum(e=>e.Quantity)==56);
                db.Fork("C"); check("grandchild traverses ancestors with checkpoint limits",db.Read("meal_sale","v").Sum(e=>e.Quantity)==13);
                db.Append(new[]{Sale(999)});
                db.Fork("C"); check("unsaved events cannot leak into reload",db.Read("meal_sale","v").Sum(e=>e.Quantity)==13);
                var before=db.Read("meal_sale").Count;
                try { db.Append(new[]{Sale(8),Sale(9) with { Kind=null! }}); } catch(InvalidDataException) { }
                check("failed batches roll back completely",db.Read("meal_sale").Count==before);
                db.Append(new[]{new HistoryEvent { Kind="staff_payment",Venue="v",Subject="employee",Name="O'Brien 🧑",Quantity=2,Money=-50,Day=1,Source="legacy_daily_import" }});
                db.Checkpoint("D","slotD");
            }
            using(var db=new HistoryStore(path))
            {
                db.Fork("D"); var payroll=db.Read("staff_payment").Single();
                check("history survives connection reopen with names and precision",payroll.Name=="O'Brien 🧑" && payroll.Money==-50 && payroll.Source=="legacy_daily_import");
                db.Append(new[] { payroll with { Quantity = 1, Money = -30, Day = 2, Source = "live" } });
                db.Checkpoint("payroll-only", "slotD");
                db.Fork("D");
                check("older save restores only its staff payments", db.Read("staff_payment").Sum(e => e.Money) == -50);
                db.Fork("payroll-only");
                var payments = db.Read("staff_payment");
                check("payroll-only recording extends a mixed legacy log", payments.Count == 2 && payments.Sum(e => e.Money) == -80);
                var ledger = new PayrollLedger();
                foreach (var e in payments)
                    if (e.Subject.Length != 0) ledger.Add(e.Venue, e.Subject, e.Name, e.Day, checked((int)e.Quantity), e.Money);
                check("restored employee breakdown retains both payment days", ledger.Payments.Count == 2 && ledger.Payments.Sum(p => p.Amount) == -80);
                db.Fork("new-game"); check("new playthrough does not inherit matching game dates",db.Read("meal_sale").Count==0);
                check("new playthrough does not inherit payroll", db.Read("staff_payment").Count == 0);
            }
            Exception? failure=null;
            using(var worker=new HistoryWorker(path,e=>failure=e))
            {
                worker.Run(db=>db.Fork("D"));
                worker.Post(db=>db.Append(new[]{Sale(1)}));
                worker.Post(db=>db.Checkpoint("worker","slot"));
                var sum=worker.Run(db=> { db.Fork("worker"); return db.Read("meal_sale","v").Sum(e=>e.Quantity); });
                check("worker checkpoints preserve FIFO event ordering",sum==14 && failure==null);
            }
        }
        finally { Directory.Delete(dir,true); }
        var crashPath = Path.Combine(Path.GetTempPath(), "hud-crash-" + Guid.NewGuid() + ".log");
        try
        {
            using (var store = new HistoryStore(crashPath))
            {
                store.Fork("new"); store.Append(new[] { Sale(2) }); store.Checkpoint("saved", "slot");
            }
            var committed = File.ReadAllBytes(crashPath);
            using (var store = new HistoryStore(crashPath))
            {
                store.Fork("saved"); store.Append(new[] { Sale(99) });
            }
            var complete = File.ReadAllBytes(crashPath);
            // Every truncation within the final frame must retain the old checkpoint.
            var lastFrame = committed.Length;
            lastFrame += 36 + BitConverter.ToInt32(complete, lastFrame); // complete fork
            var recovered = true;
            for (var cut = lastFrame + 1; cut < complete.Length; cut++)
            {
                File.WriteAllBytes(crashPath, complete[..cut]);
                using var store = new HistoryStore(crashPath);
                recovered &= new FileInfo(crashPath).Length == lastFrame;
                store.Fork("saved");
                recovered &= store.Read("meal_sale").Sum(e => e.Quantity) == 2;
            }
            check("every interrupted final-frame write recovers without leaking future events", recovered);
            complete[^1] ^= 1;
            File.WriteAllBytes(crashPath, complete);
            var rejected = false;
            try { using var store = new HistoryStore(crashPath); } catch (InvalidDataException) { rejected = true; }
            check("checksum corruption is rejected without silently deleting history", rejected && File.ReadAllBytes(crashPath).SequenceEqual(complete));
            File.WriteAllBytes(crashPath, System.Text.Encoding.ASCII.GetBytes("HUDLOG99"));
            rejected = false;
            try { using var store = new HistoryStore(crashPath); } catch (InvalidDataException) { rejected = true; }
            check("future format versions are rejected", rejected);
        }
        finally { File.Delete(crashPath); }

    }
}
