# MEP Radial Pipe Routing 1.4 — Axis 3D

Add-in C# / WPF cho Revit 2023, .NET Framework 4.8, x64. Không có bảng setup hoặc cửa sổ Pipe Routing. Chỉ hiện menu tròn khi giữ Ctrl; click/Enter tạo Pipe thật ngay qua ExternalEvent. Tên System Type hiện ngay trên radial menu; tuyến mới có thể đổi hệ bằng Ctrl+Tab.

## Cách dùng

1. **Way 1 — Pipe gốc:** gọi PI/Pipe và vẽ bình thường bằng Revit. Không nhấn Ctrl thì add-in không hủy lệnh, không tạo router, không sửa thông số Properties và không can thiệp click/Enter/Esc/wheel.
2. **Way 2 — Ctrl/radial:** trong 3D orthographic, giữ Ctrl khi đang dùng Pipe để chuyển sang radial. Đầu hở của đoạn native vừa vẽ được dùng làm nguồn, giữ type/system/diameter thực tế. Hoặc ngoài lệnh Pipe: chọn Pipe/thiết bị rồi giữ Ctrl gần connector mở (32 pixel) để vào radial trực tiếp.
3. Giữ Ctrl: chọn **R** trên vòng tròn để xoay quanh trục nguồn. Wheel chọn **T** (góc nghiêng khỏi trục): 0 / 15 / 22.5 / 30 / 45 / 60 / 90°. Mặc định T=45°. Có thể rê ra ngoài vòng tròn; thả Ctrl để khóa vector 3D. Vùng tâm/Esc hủy lượt chọn và khôi phục góc nghiêng trước.
4. Di chuyển chuột để kéo chiều dài. Click hoặc Enter tạo ngay đoạn Pipe và kết nối với nguồn, gồm fitting nếu Revit cần và có cấu hình phù hợp. Không cần thao tác Finish ẩn để ghi model.
5. Sau khi thành công, đầu hở của Pipe mới trở thành nguồn tiếp theo. Tiếp tục Ctrl/chọn hướng/kéo/click.
6. Enter giữa các đoạn hoặc Esc khi không còn preview kết thúc phiên. Esc/Backspace khi đang preview chỉ bỏ preview. Các Pipe đã tạo vẫn ở model. Kết thúc phiên rồi dùng Undo gốc của Revit để hoàn tác đoạn đã tạo.

Chỉ có radial overlay không lấy focus. Không có SetForegroundWindow, SendInput hoặc SetCursorPos. Không điều khiển chuột/bàn phím từ xa. Hook chỉ hoạt động khi Revit foreground và đúng view nguồn; input bị bỏ khi mất focus/đổi view. Bộ theo dõi connector ngoài phiên không chặn Enter/Esc/Backspace.

## Vector 3D theo trục nguồn

Không còn chọn XY/XZ/YZ hoặc tự chọn mặt phẳng theo camera trong luồng routing.

- U là hướng tiến: Pipe lấy tiếp tuyến đường tâm tại connector được chọn, đảo dấu tại đầu đầu tiên để luôn hướng ra ngoài ống. Thiết bị/fitting dùng BasisZ của connector.
- V là hướng tham chiếu cho R=0°, ban đầu lấy hướng Z chiếu vuông góc U. Nếu U gần đứng, lấy X làm tham chiếu để tránh vector suy biến. W=U×V.
- Vector = cos(T) U + sin(T) [cos(R) V + sin(R) W]. R xoay quanh U, T nghiêng khỏi U; đây là tập hướng trên một mặt nón 3D, không phải một mặt phẳng qua origin.
- T=0°: đi thẳng theo U, không phụ thuộc R. T=90°: rẽ vuông góc U. Với các góc khác, hướng vừa tiến vừa nghiêng theo R.
- Sau mỗi đoạn tạo thành công, U cập nhật theo hướng đầu hở của Pipe mới. V/W được vận chuyển bằng phép quay tối thiểu từ trục cũ sang trục mới để giữ tham chiếu xoay liên tục khi đi qua ống đứng/xiên; không reset theo camera hoặc world axes.
- Preview hiển thị trục U/V/W, mặt nón lựa chọn và mũi tên vector thật. Menu hiển thị AXIS 3D, R và T. Camera chỉ ảnh hưởng phép đo chiều dài từ màn hình, không thay vector đã khóa.

Nếu bắt đầu ở điểm tự do chưa có Pipe/connector, chưa tồn tại trục nguồn; dùng RightDirection của view tại thời điểm bắt đầu làm trục ban đầu. Sau đoạn đầu, dùng trục Pipe thật. Hướng gần view-normal vẫn không kéo được chiều dài từ chuột; xoay view hoặc đổi R/T. Chưa có nhập số góc tùy ý/chiều dài ở bản không panel.

## Điểm khởi chạy và thông số

BeforeExecuted chỉ ghi nhận PI bắt đầu, **không đặt Cancel và không tạo phiên radial**. DocumentChanged chỉ đọc các Pipe mới/được sửa để chụp đầu hở và thông số thực tế. Chỉ Ctrl mới yêu cầu kết thúc tool native và chuyển sang ExternalEvent/radial. Lượt Pipe native được kết thúc bằng hai cặp thông điệp Escape gửi riêng tới HWND Revit đang foreground; không gửi phím toàn hệ thống, không chuyển focus hoặc di chuyển chuột. Handoff bị bỏ khi mất focus/đổi view/document; chờ quá 3 giây thì bỏ yêu cầu, không thử gửi thêm phím.

Radial là chế độ riêng sau khi chuyển, không khóa trực tiếp đầu ống preview của native command. Bản hiện tại chỉ nhận chắc chắn điểm từ connector hoặc Pipe đã ghi model; nếu Ctrl trước khi tạo đoạn native đầu tiên và chưa có connector nguồn, cần chọn điểm đầu lại sau khi chuyển. Không lấy tọa độ giả từ màn hình. Sau radial, Enter/Esc kết thúc phiên; gọi PI lại để trở về Pipe gốc.

Plan/section/perspective giữ Pipe native và không có radial routing. Grip connector còn phụ thuộc command Revit thực sự gọi; chọn phần tử rồi Ctrl gần connector là đường khởi chạy trực tiếp.

- Pipe nguồn: kế thừa type/system/level và đường kính connector.
- Thiết bị: Pipe Type mặc định của document, system khớp classification, level thiết bị hoặc level gần connector nhất. Không suy ra được system thì báo lỗi.
- Điểm mới không có nguồn: ưu tiên thông số của lần tạo thành công trước trong document (kiểm tra ID còn tồn tại), rồi Pipe gần nhất. Nếu chưa có Pipe, dùng Pipe Type mặc định/100 mm, chọn Hydronic Supply nếu có, tiếp theo Domestic Cold Water rồi hệ đầu tiên theo tên. Nhiều System Type không còn là lỗi. Tên system hiện dưới menu Ctrl; Tab khi giữ Ctrl đổi hệ trước đoạn đầu tiên. Không hiện bảng setup. Native Properties không tự điều khiển router sau handoff.
- Chọn điểm đầu dùng work plane hiện tại và snap endpoint/intersection. Nếu chưa có work plane, tạo tạm mặt phẳng song song màn hình qua View.Origin, rollback sau khi chọn/Esc. Điểm cách connector mở dưới 1 mm được nhận làm nguồn.

## Tạo ống và fitting

Mỗi click hợp lệ tạo một transaction riêng. Đoạn thẳng từ connector dùng `Pipe.Create(Document, pipeTypeId, levelId, startConnector, endPoint)`. Khi đổi hướng, tạo Pipe hai điểm, đặt đường kính rồi gọi `NewElbowFitting` để tạo co thật. Kiểm tra `MechanicalFitting.PartType == Elbow` và hai connector của co nối tới đúng hai đầu ống. Đi ngược vào nguồn bị từ chối. Không coi kết nối trực tiếp ở góc là đã có fitting.

Sau khi tạo, kiểm tra đường kết nối vật lý tới phần tử nguồn; ở chỗ đổi hướng, đường này bắt buộc đi qua Elbow thật, không chấp nhận union hoặc co ở nhánh không liên quan; kiểm tra có đầu hở đúng endpoint để tiếp tục. Bất kỳ lỗi tạo/nối/commit đều rollback toàn bộ đoạn vừa thử, kể cả phần Pipe có thể đã được API tạo trước khi ném lỗi. Những đoạn đã tạo thành công trước đó được giữ. Preview lỗi vẫn còn để đổi hướng hoặc Esc.

Nguồn được kiểm tra lại về identity, vị trí, hướng, đường kính và trạng thái mở trước mỗi lần tạo. Lỗi hiển thị Pipe Type, diameter, số elbow rules và thông báo Revit. Không tự sửa routing preferences của model hoặc tự giữ ống rời khi không nối được.

**Không thể bảo đảm fitting nếu model không có family/routing rule hỗ trợ đúng kích thước và góc.** Không tự tải family hoặc thay Pipe Type. Kiểm tra chiều dài tối thiểu và chồng đồng tuyến với các đoạn đã tạo trong phiên; chưa kiểm tra vật cản hoặc nối tự động tới connector đích.

## Chẩn đoán

Log chính: `%LOCALAPPDATA%\MEP_Radial_Routing\routing.log`. Ghi version/DLL đã nạp, handoff Pipe, nguồn/thông số, hướng khóa, tạo thành công và lỗi. Log lỗi tương thích cũ: `%TEMP%\MEP_Radial_Routing.log`.

## Cấu trúc

- `Core/Geometry.cs`: vector, frame quanh trục nguồn, roll/tilt mặt nón, transport frame qua các đoạn, projection, hit test, RouteSession. Các helper mặt phẳng cũ chỉ còn phục vụ kiểm tra hồi quy, không dùng để khóa hướng routing.
- `Revit/NativeRoutingHost.cs`: quan sát PI không hủy, DocumentChanged snapshot nguồn native, Ctrl handoff có giới hạn thời gian, theo dõi connector lúc Idling và log.
- `Revit/Routing.cs`: chọn điểm, preview, tạo từng đoạn ngay khi click, chuyển nguồn sang Pipe mới, kết thúc phiên.
- `Revit/Services.cs`: snapshot connector, nhận thông số, tạo/nối Pipe bằng connector overload, kiểm tra liên kết và rollback.
- `Revit/PreviewRenderer.cs`: DirectContext3D, stroke snapshot bất biến, chia batch, wireframe/marker/trục.
- `UI/Views.cs`: chỉ có radial overlay. Không còn SettingsWindow.
- `UI/InputController.cs`: hook primitive input, dispatch sau hook, gộp mouse-move, giữ cặp click, chặn input lặp trong lúc chờ ghi model.

## Build / test / cài

Cần Visual Studio + .NET Framework 4.8 targeting pack + Revit 2023; không cần NuGet. Tên project cũ được giữ, output `MEP_Radial_Routing_ver1.dll`. Class1.cs không nằm trong build.

```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' .\MEP_Direct_Context_3D_ver1.csproj /t:Build /p:Configuration=Release /p:OutputPath=bin\Axis3D\ /p:UseSharedCompilation=false
.\scripts\test.ps1
.\scripts\install.ps1 -AssemblyPath .\bin\Axis3D\MEP_Radial_Routing_ver1.dll
```

Nếu API ở nơi khác, thêm `/p:RevitApiPath=...`. Manifest cài trong `%APPDATA%\Autodesk\Revit\Addins\2023`; khởi động lại Revit để nạp DLL mới. Không đóng Revit tự động. Không chép RevitAPI.dll/RevitAPIUI.dll cùng add-in. Manifest gốc có `__ASSEMBLY_PATH__` là template.

## Kiểm chứng

09/11/2026: 799 assertions hình học/projection/menu/state và 20 assertions input. Có kiểm tra các trục ngang/đứng/đứng ngược/xiên/gần đứng; mọi preset R/T có vector đơn vị và góc với trục nguồn đúng T; ba hướng trên mặt nón không đồng phẳng; T=0 đi thẳng; chuyển frame qua ống đứng giữ đúng tham chiếu xoay. Giữ các kiểm tra hồi quy hit test, projection, graph fitting và trạng thái tuyến. Input tests không cài hook thật hoặc điều khiển thiết bị. Build với DLL API Revit 2023 thực tế.

**Chưa chạy được kiểm thử tương tác/transaction fitting trên model thực tế vì công cụ không kết nối được Revit client.** Unit test/build không chứng minh fitting trên model có thể tạo. Log 1.2 xác nhận các lần tạo được báo thành công chỉ có một phần tử Pipe; code cũ chấp nhận kết nối trực tiếp ở góc. Bản 1.2.1 thêm ca regression cho lỗi này và yêu cầu Elbow thật. Góc đã tạo trong model bằng bản cũ không tự được sửa khi nạp DLL mới.

Checklist model bản sao: chọn đúng từng connector; Pipe gốc khởi chạy được; vector theo trục nguồn trong mọi góc nhìn; rê xa vẫn khóa hướng; click tạo ngay Pipe/fitting; đầu hở tiếp tục được; thiếu fitting phải rollback đoạn vừa thử; Esc giữ các đoạn đã tạo; Alt-Tab/đổi view/đóng document dọn hoặc tạm ngưng input; nhiều màn hình/DPI không làm lệch hit test; sau restart log ghi version 1.4.0.0 và đúng DLL Axis3D.

Bản 1.2.2 sửa lỗi tuyến mới trong template có nhiều System Type. Connector nguồn vẫn chỉ nhận hệ cùng classification; không tự gán Hydronic Supply cho connector có classification khác. System mặc định được hiển thị và có thể đổi trong menu Ctrl, không yêu cầu chọn nguồn trước.

Checklist hai way: PI không Ctrl phải giữ trọn luồng Revit; sau một đoạn native, Ctrl chuyển từ đúng đầu hở; Ctrl nhanh vẫn giữ tâm/menu và điểm thả; Ctrl không có nguồn cần chọn điểm đầu; ngoài lệnh Pipe, chọn connector + Ctrl phải khởi chạy được; Esc/Alt-Tab/đổi view không để lại handoff chờ vô hạn; đổi hướng radial phải có Elbow thật. Chưa kiểm thử handoff bằng thông điệp cửa sổ trong Revit vì kết nối Revit client chưa hoạt động.

Checklist Axis 3D: lấy cả hai đầu cùng Pipe, U phải ngược nhau và hướng ra ngoài; ống xiên không kéo về trục model; wheel đổi T chứ không đổi plane; R=0/T=0 đi thẳng; T=90 tạo góc vuông; tạo liên tiếp hai đoạn xiên rồi kiểm tra U theo đoạn mới; xoay camera sau khóa không đổi vector; Cancel menu khôi phục T; PI không Ctrl vẫn native; mỗi chỗ đổi hướng phải có Elbow thật. Chưa kiểm thử tương tác 1.4 trong Revit.
