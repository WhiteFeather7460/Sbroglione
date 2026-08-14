using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls.Shapes;
using ReactiveUI;

namespace GetStartedApp.Utils
{
    public class FileSystemItem : ReactiveObject
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public string? Size { get; set; }
        public DateTime LastModified { get; set; }
        public bool IsDirectory { get; set; }
        public string Icon => IsDirectory ? "📁" : "📄";

        private string? _checkSum;
        public string? CheckSum
        {
            get => _checkSum;
            set => this.RaiseAndSetIfChanged(ref _checkSum, value);
        }

        /*
        // ad es. nella tua ViewModel, prima di iniziare la copia
        public async Task PrepareAndCopyAsync(CancellationToken ct)
        {
            // fsList: IEnumerable<FileSystemItem> con i file da copiare (salta le directory)
            var progress = new Progress<(int done, int total)>(p =>
            {
                // aggiorna una progress bar se vuoi (p.done / (double)p.total)
                CopyPreparationProgress = p;
            });

            await ChecksumPrecalculator.PrecomputeChecksumsAsync(fsList, "SHA256",
                maxDegreeOfParallelism: Environment.ProcessorCount - 1,
                progress: progress,
                ct: ct);

            // qui i CheckSum degli item file sono già pronti
            await CopyFilesAsync(fsList, ct);
        }
         */
    }
}
