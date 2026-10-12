import { useState } from "react";
import { collectBackupData, restoreBackupData } from "../utils/backup";

export default function LibraryBackup() {
  const [message, setMessage] = useState("");
  const exportBackup = () => {
    const url = URL.createObjectURL(new Blob([JSON.stringify({ version: 1, data: collectBackupData() }, null, 2)], { type: "application/json" }));
    const link = document.createElement("a"); link.href = url; link.download = "streambert-backup.json"; link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  };
  const importBackup = async event => {
    const file = event.target.files?.[0]; if (!file) return;
    try {
      if (file.size > 5 * 1024 * 1024) throw new Error("File too large");
      const backup = JSON.parse(await file.text());
      if (backup.version !== 1 || !backup.data || typeof backup.data !== "object") throw new Error("Invalid backup");
      restoreBackupData(backup.data); window.location.reload();
    } catch { setMessage("导入失败，请选择有效的 Streambert 备份文件。"); }
    event.target.value = "";
  };
  return <div className="web-actions">
    <button className="btn btn-ghost" onClick={exportBackup}>导出备份</button>
    <label className="btn btn-ghost">导入备份<input aria-label="导入备份" type="file" accept="application/json,.json" onChange={importBackup} hidden /></label>
    <span role="status">{message}</span>
  </div>;
}
