import { useLocation } from 'react-router-dom';
import { allNavItems } from './nav';
import { Icon } from './AppLayout';

function ComingSoon() {
    const { pathname } = useLocation();
    const item = allNavItems().find(i => i.to === pathname);

    return (
        <div className="coming-soon card">
            <span className="coming-soon-mark">
                <Icon name={item?.icon ?? 'bolt'} />
            </span>
            <h2>{item?.label ?? 'This module'}</h2>
            <p>{item?.desc ?? ''}</p>
            <span className="chip chip-yellow">Coming in an upcoming build</span>
        </div>
    );
}

export default ComingSoon;
