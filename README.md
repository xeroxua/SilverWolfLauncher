# Silver Wolf Launcher 🐺
*Advanced Launcher for Honkai: Star Rail Private Servers*

![Silver Wolf Launcher Preview](Assets/preview.png)

**Silver Wolf Launcher** คือเครื่องมือระดับพรีเมียมที่ออกแบบมาเพื่อให้การจัดการและเข้าเล่น **Honkai: Star Rail Private Server** เป็นเรื่องง่าย สวยงาม และทรงพลัง พัฒนาด้วยเทคโนโลยีล่าสุดอย่าง **.NET 10.0** และ **WPF** พร้อมดีไซน์ที่ได้รับแรงบันดาลใจจากสไตล์ Mobile UI ของเกมโดยตรง

> [!IMPORTANT]
> **Project Status**: ปัจจุบันอยู่ในสถานะ **Early Access** ผลงานชิ้นนี้พัฒนาโดยอิสระเพื่อชุมชน หากพบปัญหาท่านสามารถแจ้งได้ที่ [Discord Community](https://discord.gg/QwfTnEdAtN)

---

## ✨ คุณสมบัติที่โดดเด่น (Key Features)

*   **🎨 Premium HSR Aesthetic**: อินเทอร์เฟซแบบ Glassmorphism ที่สวยงามและลื่นไหล พร้อมระบบอนิเมชั่นที่ถอดแบบมาจากตัวเกม
*   **⚡ One-Click Launch**: ระบบเริ่มเกมที่รวบรวมการเปิด Proxy และ Private Server ไว้ในคลิกเดียว
*   **🛡️ Robust Watchdog**: ระบบเฝ้าระวังอัตโนมัติ หาก Server หรือ Proxy ปิดตัวลง Launcher จะพยายาม Re-launch ให้ทันทีภายใน 5 วินาที
*   **🩹 Smart Patcher**: รองรับการลง Patch (.7z, .zip) แบบอัตโนมัติ พร้อมระบบจัดการความขัดแย้งของไฟล์ผ่าน `hpatchz`
*   **🌐 Seamless Web Integration**: รวมเครื่องมือเสริม (SR Tools) และคู่มือการเล่นผ่าน WebView2 พร้อมระบบ **Airspace-Fix** (หน้าต่างแจ้งเตือนจะไม่ถูกเว็บทับ)
*   **🌍 Localization Expert**: รองรับการเปลี่ยนภาษาทั้งตัวโปรแกรมและตัวเกม (Text/Voice) อย่างสมบูรณ์
*   **🌙 System Tray Integration**: ย่อโปรแกรมลง Tray พร้อมเมนูทางลัดเพื่อประหยัดทรัพยากรเครื่อง

---

## 🛠️ การติดตั้งและเริ่มต้นใช้งาน (Getting Started)

### สำหรับผู้ใช้งานทั่วไป (Users)
1.  **Set Game Path**: เข้าไปที่ส่วนจัดการตำแหน่งไฟล์ เลือกไฟล์ `.exe` ของเกม Honkai: Star Rail
2.  **Environment Check**: โปรแกรมจะตรวจสอบไฟล์ Private Server ให้อัตโนมัติ หากไม่มีสามารถกด **Update** เพื่อติดตั้งได้ทันที
3.  **Launch**: กดปุ่ม **LAUNCH** เพื่อเริ่มการเดินทางข้ามกลุ่มดาว

### สำหรับนักพัฒนา (Developers)
หากท่านต้องการร่วมพัฒนาหรือคอมไพล์โค้ดด้วยตนเอง:
*   **Requirements**: Visual Studio 2022 หรือ .NET 10.0 SDK
*   **Build**:
    ```bash
    dotnet build -c Release
    ```

---

## 🛡️ ความปลอดภัยและความเป็นส่วนตัว (Security & Privacy)

*   **No Data Collections**: เราไม่มีการเก็บข้อมูลส่วนตัวใดๆ ไฟล์ `config.json` จะถูกเก็บไว้เฉพาะในเครื่องของผู้ใช้เท่านั้น
*   **Open Source**: ท่านสามารถตรวจสอบโค้ดทั้งหมดได้เพื่อความมั่นใจในความโปร่งใส

---

## 📜 สัญญาอนุญาต (License)

โปรเจกต์นี้เปิดเผยซอร์สโค้ดภายใต้สัญญาอนุญาต **Apache License 2.0** ท่านสามารถนำไปศึกษาและพัฒนาต่อยอดได้ตามเงื่อนไขที่กำหนด

---

## 💬 การสนับสนุนและชุมชน (Support)

*   **Discord**: [Silver Wolf Community](https://discord.gg/QwfTnEdAtN)
*   **Developer**: [Follow xeroxua on GitHub](https://github.com/xeroxua)

---

**Crafted with 💖 by xeroxua**
*Disclaimer: Silver Wolf Launcher เป็นโปรเจกต์อิสระ ไม่ได้มีความเกี่ยวข้องกับ HoYoverse หรือบริษัทเจ้าของลิขสิทธิ์เกมแต่อย่ายใด*
